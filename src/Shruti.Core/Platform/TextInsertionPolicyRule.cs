namespace Shruti.Core.Platform;

public sealed class TextInsertionPolicyRule
{
    private readonly HashSet<string> _processNames;

    public TextInsertionPolicyRule(
        string id,
        IEnumerable<string> processNames,
        TextInsertionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(processNames);

        Id = id;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string processName in processNames)
        {
            string normalizedProcessName = TextInsertionPolicyEvaluator.NormalizeProcessName(processName);
            if (normalizedProcessName.Length > 0)
            {
                _processNames.Add(normalizedProcessName);
            }
        }

        if (_processNames.Count == 0)
        {
            throw new ArgumentException("At least one process name is required.", nameof(processNames));
        }
    }

    public string Id { get; }

    public TextInsertionPolicy Policy { get; }

    public IReadOnlyCollection<string> ProcessNames => _processNames;

    public bool Matches(FocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        string processName = TextInsertionPolicyEvaluator.NormalizeProcessName(target.ProcessName);
        return processName.Length > 0 && _processNames.Contains(processName);
    }
}
