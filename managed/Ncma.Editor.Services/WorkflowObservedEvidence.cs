using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ncma.Editor.Services;

internal static class WorkflowObservedEvidence
{
    internal static WorkflowProcessOutcome Outcome(WorkflowVerificationTask task, WorkflowBuildStatus status, WorkflowBuildObservation actual)
    {
        if (status.JobId != task.Binding.JobId || !status.ExecutionDrained || !actual.ExecutionDrained ||
            status.BuildStarted != actual.BuildStarted || status.ExitCode != actual.ExitCode || status.Phase is not (WorkflowBuildPhase.AwaitingClose or WorkflowBuildPhase.Retained))
            throw new IOException("workflow_evidence_execution_not_terminal");
        // Abandoning the scheduling slot permanently disqualifies success, even
        // if the old process later reports exit=0 and drains naturally.
        if (status.Abandoned) return WorkflowProcessOutcome.ObservationLost;
        return status.Outcome switch {
            WorkflowBuildOutcome.Cancelled => WorkflowProcessOutcome.Cancelled,
            WorkflowBuildOutcome.TimedOut => WorkflowProcessOutcome.TimedOut,
            _ when !actual.BuildStarted => WorkflowProcessOutcome.NotStarted,
            // A drained exit=0 cannot erase a stale/rejected/failed queue outcome.
            _ when status.Outcome is not (WorkflowBuildOutcome.ProcessExited or WorkflowBuildOutcome.ProcessFailed) => WorkflowProcessOutcome.ObservationLost,
            _ when actual.ExitCode is null => WorkflowProcessOutcome.ObservationLost,
            _ => WorkflowProcessOutcome.Exited };
    }
    internal static (int? Warnings, int? Errors) CompilerCounts(ReadOnlySpan<byte> stdout, ReadOnlySpan<byte> stderr)
    {
        string text = new UTF8Encoding(false, true).GetString(stdout) + "\n" + new UTF8Encoding(false, true).GetString(stderr);
        static int? Read(string text, string kind) {
            var matches = Regex.Matches(text, @"(?m)^\s*([0-9]+) " + kind + @"\(s\)[ \t]*\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return matches.Count == 1 && int.TryParse(matches[0].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : null;
        }
        return (Read(text, "Warning"), Read(text, "Error"));
    }
    internal static string Code(Exception error)
    {
        if (error.Message.Length is > 9 and <= 96 && error.Message.StartsWith("workflow_", StringComparison.Ordinal) &&
            error.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return error.Message;
        string type = error.GetType().Name;
        return type.Length <= 96 && type.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? type : "workflow_observation_failed";
    }
}
