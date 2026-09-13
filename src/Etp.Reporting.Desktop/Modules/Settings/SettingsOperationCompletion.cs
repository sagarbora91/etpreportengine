namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    private bool AllowConnectionCandidate(string candidate)
    {
        if (string.Equals(candidate, session.ConnectionString, StringComparison.OrdinalIgnoreCase)) return true;
        if (!integrationsLoaded && CanChangeDatabase?.Invoke() != false) return true;
        ConnectionResult.Text = "This session contains work from the current database. Close ETP after saving your work, then open Settings → Database connection before opening another task to change databases. The active connection is unchanged.";
        return false;
    }

    private async Task NotifyCompletedAsync(SettingsWorkspaceOperation operation, bool succeeded)
    {
        if (OperationCompletedAsync is not { } completed) return;
        try { await completed(operation, succeeded); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Workspace", "POST_OPERATION_REFRESH_FAILED");
            ConnectionResult.Text += " The operation result is retained, but the follow-up display refresh failed. Reopen the task to refresh its status.";
        }
    }
}
