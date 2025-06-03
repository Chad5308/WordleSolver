using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WordleSolver.Strategies
{
    /// <summary>
    /// An advanced Wordle solving strategy designed to efficiently find the secret word.
    ///
    /// Algorithm Overview:
    /// 1. Initialization (`Reset`):
    ///    - The solver maintains a list of `_remainingWords`, initially populated with all words
    ///      from the official `wordle.txt` dictionary (stored in `WordList`).
    ///
    /// 2. First Guess (`PickNextGuess` - first turn):
    ///    - A strategically chosen starting word, "arose", is always used. This word contains
    ///      five distinct, common vowels and consonants, aiming to maximize initial information.
    ///
    /// 3. Subsequent Guesses (`PickNextGuess` - after first turn):
    ///    a. Filtering `_remainingWords`:
    ///       - Based on the feedback (Correct, Misplaced, Unused letters) from the `previousResult`,
    ///         the `_remainingWords` list is pruned.
    ///       - A `CheckCompatibility` method is employed: for each word in `_remainingWords`, it simulates
    ///         the feedback that would have been generated if *that word* were the true answer and the
    ///         `previousResult.Word` was guessed against it.
    ///       - If the simulated feedback matches the `previousResult.LetterStatuses` exactly, the word
    ///         remains a possibility; otherwise, it's removed.
    ///       - The `GenerateFeedback` helper method accurately mimics Wordle's logic for providing
    ///         feedback, including correct handling of duplicate letters.
    ///
    ///    b. Selecting the Next Guess (`SelectBestCandidateFrom`):
    ///       - If only one word remains in `_remainingWords`, it is chosen as it must be the answer.
    ///       - Otherwise, a heuristic is applied to select the most informative word from the
    ///         current `_remainingWords`:
    ///         - Calculate the frequency of each distinct character appearing in the set of all
    ///           `_remainingWords`.
    ///         - Score each candidate word in `_remainingWords` by summing the frequencies of its
    ///           distinct letters.
    ///         - The word with the highest score is chosen as the next guess. This approach
    ///           favors words that are likely to test common letters within the remaining
    ///           solution space, thereby maximizing information gain.
    ///
    /// This strategy aims for a low average number of guesses by systematically eliminating
    /// non-viable words and making informed choices for subsequent guesses.
    /// </summary>
    public sealed class AwesomeStudentSolver : IWordleSolverStrategy
    {
        /// <summary>Absolute or relative path of the word-list file.</summary>
        private static readonly string WordListPath = Path.Combine("data", "wordle.txt");

        /// <summary>
        /// In-memory dictionary of all valid five-letter words, loaded once.
        /// This list remains unchanged throughout the execution and serves as the master list.
        /// </summary>
        private static readonly List<string> WordList = LoadWordList();

        /// <summary>
        /// List of words that are still considered possible answers based on feedback received so far.
        /// This list is filtered and reduced after each guess.
        /// </summary>
        private List<string> _remainingWords = new();

        /// <summary>
        /// Loads the dictionary from disk, filtering to distinct five-letter lowercase words.
        /// This is called once to populate the static `WordList`.
        /// </summary>
        private static List<string> LoadWordList()
        {
            if (!File.Exists(WordListPath))
                throw new FileNotFoundException($"Word list not found at path: {WordListPath}");
            return File.ReadAllLines(WordListPath)
                .Select(w => w.Trim().ToLowerInvariant())
                .Where(w => w.Length == 5)
                .Distinct()
                .ToList();
        }

        /// <inheritdoc/>
        public void Reset()
        {
            // When a new game starts, reset the list of remaining possible words
            // to a copy of the full master WordList.
            _remainingWords = new List<string>(WordList);
        }

        /// <summary>
        /// Determines the next word to guess given feedback from the previous guess.
        /// </summary>
        /// <param name="previousResult">
        /// The <see cref="GuessResult"/> returned by the game engine for the last guess
        /// (or <see cref="GuessResult.Default"/> if this is the first turn).
        /// </param>
        /// <returns>A five-letter lowercase word to be guessed next.</returns>
        public string PickNextGuess(GuessResult previousResult)
        {
            // Ensure the previous guess was valid before proceeding.
            if (!previousResult.IsValid && previousResult.GuessNumber > 0)
                throw new InvalidOperationException("PickNextGuess should not be called if the previous result was an invalid word, unless it's the start of the game.");

            // Handle the first guess of the game.
            if (previousResult.GuessNumber == 0)
            {
                // Always start with a pre-determined strong opening word.
                return "audio";
            }

            // For subsequent guesses, filter the list of _remainingWords based on the feedback.
            // The previousResult.Word is the word that was just guessed.
            // The previousResult.LetterStatuses is the feedback for that guess.
            _remainingWords = _remainingWords.Where(candidateWord =>
                CheckCompatibility(candidateWord, previousResult.Word, previousResult.LetterStatuses))
                .ToList();

            // If, after filtering, no words remain, something went wrong.
            if (!_remainingWords.Any())
            {
                throw new InvalidOperationException("No remaining words after filtering. This suggests a logic error or an unexpected game state.");
            }

            // From the (now reduced) list of _remainingWords, select the best candidate.
            return SelectBestCandidateFrom(_remainingWords);
        }

        /// <summary>
        /// Selects the best candidate word from the given list of possibilities.
        /// </summary>
        /// <param name="currentCandidates">The list of currently possible words after filtering (this will be `_remainingWords`).</param>
        /// <returns>The selected word to guess next.</returns>
        private string SelectBestCandidateFrom(List<string> currentCandidates)
        {
            // If only one word remains, it must be the answer.
            if (currentCandidates.Count == 1)
            {
                return currentCandidates.First();
            }

            // Heuristic: Choose the candidate word that maximizes a score based on
            // the frequency of its distinct letters within the *current set of candidates*.
            var charFrequenciesInCandidates = new Dictionary<char, int>();
            foreach (var word in currentCandidates)
            {
                foreach (var c in word.Distinct())
                {
                    charFrequenciesInCandidates[c] = charFrequenciesInCandidates.GetValueOrDefault(c, 0) + 1;
                }
            }

            string bestWord = currentCandidates.First();
            double maxScore = -1.0;

            // Score each candidate word.
            foreach (var candidateWord in currentCandidates)
            {
                double score = candidateWord.Distinct().Sum(c => charFrequenciesInCandidates.GetValueOrDefault(c, 0));
                if (score > maxScore)
                {
                    maxScore = score;
                    bestWord = candidateWord;
                }
            }
            return bestWord;
        }

        /// <summary>
        /// Checks if a potential answer (a candidate word) is compatible with the feedback
        /// received from a previous guess.
        /// </summary>
        /// <param name="potentialAnswer">The candidate word from `_remainingWords` to check.</param>
        /// <param name="guessedWord">The word that was actually guessed in the previous turn.</param>
        /// <param name="actualFeedback">The feedback (LetterStatus array) received for the `guessedWord`.</param>
        /// <returns>True if `potentialAnswer` is compatible with the `actualFeedback`, false otherwise.</returns>
        private bool CheckCompatibility(string potentialAnswer, string guessedWord, LetterStatus[] actualFeedback)
        {
            LetterStatus[] simulatedFeedback = GenerateFeedback(guessedWord, potentialAnswer);
            return simulatedFeedback.SequenceEqual(actualFeedback);
        }

        /// <summary>
        /// Simulates the Wordle feedback mechanism for a given guess against a potential answer.
        /// This logic mirrors the feedback generation in `WordleService.cs`.
        /// </summary>
        /// <param name="guess">The guessed word.</param>
        /// <param name="answer">The potential secret answer to check against.</param>
        /// <returns>An array of <see cref="LetterStatus"/> indicating the feedback for each letter in the guess.</returns>
        private LetterStatus[] GenerateFeedback(string guess, string answer)
        {
            var statuses = new LetterStatus[5];
            var answerCharCounts = answer.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

            // Pass 1: Identify Correct letters (Green)
            for (int i = 0; i < 5; i++)
            {
                if (guess[i] == answer[i])
                {
                    statuses[i] = LetterStatus.Correct;
                    answerCharCounts[guess[i]]--;
                }
                else
                {
                    statuses[i] = LetterStatus.Unused;
                }
            }

            // Pass 2: Identify Misplaced (Yellow) letters
            for (int i = 0; i < 5; i++)
            {
                if (statuses[i] == LetterStatus.Correct)
                {
                    continue;
                }

                if (answerCharCounts.TryGetValue(guess[i], out int remainingCount) && remainingCount > 0)
                {
                    statuses[i] = LetterStatus.Misplaced;
                    answerCharCounts[guess[i]]--;
                }
            }
            return statuses;
        }
    }
}