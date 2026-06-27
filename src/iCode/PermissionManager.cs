namespace iCode;

public enum PermissionGrant { Once, Session, Always }

public class PermissionManager
{
    private readonly PermissionStore _store;
    private readonly HashSet<string> _alwaysGranted;
    private readonly HashSet<string> _sessionGranted = new(StringComparer.Ordinal);

    // Null means non-interactive mode (subagent): only persistent/session grants are accepted.
    private readonly Func<string, string, Task<PermissionGrant?>>? _requestPermission;

    public PermissionManager(PermissionStore store, Func<string, string, Task<PermissionGrant?>>? requestPermission)
    {
        _store = store;
        _alwaysGranted = new HashSet<string>(_store.LoadAlwaysGranted(), StringComparer.Ordinal);
        _requestPermission = requestPermission;
    }

    // Returns true if the action may proceed.
    public async Task<bool> RequestAsync(string toolName, string preview)
    {
        if (_alwaysGranted.Contains(toolName) || _sessionGranted.Contains(toolName))
            return true;

        if (_requestPermission == null)
            return false;

        var grant = await _requestPermission(toolName, preview);
        if (grant == null)
            return false;

        switch (grant.Value)
        {
            case PermissionGrant.Session:
                _sessionGranted.Add(toolName);
                break;
            case PermissionGrant.Always:
                _alwaysGranted.Add(toolName);
                _store.SaveAlwaysGranted(_alwaysGranted);
                break;
        }
        return true;
    }
}
