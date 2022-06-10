using System.Collections.Immutable;
using System.Threading;

namespace Mewdeko.Modules.Games.Common.Hangman;

public sealed class Hangman : IDisposable
{
    private readonly TaskCompletionSource<bool> _endingCompletionSource = new();

    private readonly SemaphoreSlim _locker = new(1, 1);

    private readonly HashSet<char> _previousGuesses = new();

    private readonly HashSet<ulong> _recentUsers = new();

    private Phase currentPhase = Phase.Active;

    public Hangman(string type, TermPool? tp = null)
    {
        TermType = type.Trim().ToLowerInvariant().ToTitleCase();
        TermPool = tp ?? new TermPool();
        Term = TermPool.GetTerm(type);
    }

    public string TermType { get; }
    public TermPool TermPool { get; }
    public HangmanObject Term { get; }

    public string ScrambledWord =>
        $"`{string.Concat(Term.Word.Select(c => { if (c == ' ') return " \u2000"; if (!(char.IsLetter(c) || char.IsDigit(c))) return $" {c}"; c = char.ToLowerInvariant(c); return _previousGuesses.Contains(c) ? $" {c}" : " ◯"; }))}`";

    public Phase CurrentPhase
    {
        get => currentPhase;
        set
        {
            if (value == Phase.Ended)
                _endingCompletionSource.TrySetResult(true);

            currentPhase = value;
        }
    }

    public uint Errors { get; private set; }
    public uint MaxErrors { get; } = 6;
    public ImmutableArray<char> PreviousGuesses => _previousGuesses.ToImmutableArray();

    public Task EndedTask => _endingCompletionSource.Task;

    public void Dispose()
    {
        OnGameEnded = null;
        OnGuessFailed = null;
        OnGuessSucceeded = null;
        OnLetterAlreadyUsed = null;
        _previousGuesses.Clear();
        _recentUsers.Clear();
        // _locker.Dispose();
    }

    public event Func<Hangman, string, Task> OnGameEnded = delegate { return Task.CompletedTask; };
    public event Func<Hangman, string, char, Task> OnLetterAlreadyUsed = delegate { return Task.CompletedTask; };
    public event Func<Hangman, string, char, Task> OnGuessFailed = delegate { return Task.CompletedTask; };
    public event Func<Hangman, string, char, Task> OnGuessSucceeded = delegate { return Task.CompletedTask; };

    private void AddError()
    {
        if (++Errors > MaxErrors)
        {
            var _ = OnGameEnded(this, null);
            CurrentPhase = Phase.Ended;
        }
    }

    public string GetHangman() =>
        $@". ┌─────┐
.┃...............┋
.┃...............┋
.┃{(Errors > 0 ? ".............😲" : "")}
.┃{(Errors > 1 ? "............./" : "")} {(Errors > 2 ? "|" : "")} {(Errors > 3 ? "\\" : "")}
.┃{(Errors > 4 ? "............../" : "")} {(Errors > 5 ? "\\" : "")}
/-\";

    public async Task Input(ulong userId, string userName, string input)
    {
        if (CurrentPhase == Phase.Ended)
            return;

        if (string.IsNullOrWhiteSpace(input))
            return;

        input = input.Trim().ToLowerInvariant();

        await _locker.WaitAsync().ConfigureAwait(false);
        try
        {
            if (CurrentPhase == Phase.Ended)
                return;

            if (input.Length > 1) // tried to guess the whole word
            {
                if (input != Term.Word) // failed
                    return;

                var _ = OnGameEnded(this, userName);
                CurrentPhase = Phase.Ended;
                return;
            }

            var ch = input[0];

            if (!char.IsLetterOrDigit(ch))
                return;

            if (!_recentUsers.Add(userId)) // don't let a single user spam guesses
                return;

            if (!_previousGuesses.Add(ch)) // that letter was already guessed
            {
                var _ = OnLetterAlreadyUsed?.Invoke(this, userName, ch);
            }
            else if (!Term.Word.Contains(ch)) // guessed letter doesn't exist
            {
                var _ = OnGuessFailed?.Invoke(this, userName, ch);
                AddError();
            }
            else if (Term.Word.All(_ => _previousGuesses.IsSupersetOf(Term.Word.ToLowerInvariant()
                         .Where(char.IsLetterOrDigit))))
            {
                var _ = OnGameEnded.Invoke(this, userName); // if all letters are guessed
                CurrentPhase = Phase.Ended;
            }
            else // guessed but not last letter
            {
                var _ = OnGuessSucceeded?.Invoke(this, userName, ch);
                _recentUsers.Remove(userId); // he can guess again right away
                return;
            }

            await Task.Run(async () =>
            {
                await Task.Delay(3000).ConfigureAwait(false); // remove the user from the spamlist after 5 seconds
                _recentUsers.Remove(userId);
            });
        }
        finally
        {
            _locker.Release();
        }
    }

    public async Task Stop()
    {
        await _locker.WaitAsync().ConfigureAwait(false);
        try
        {
            CurrentPhase = Phase.Ended;
        }
        finally
        {
            _locker.Release();
        }
    }
}