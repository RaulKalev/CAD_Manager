using System;

namespace CAD_Manager.Core
{
    /// <summary>
    /// Presentation-safe state for the main context/status row.
    /// </summary>
    public sealed class OperationStatusState
    {
        public OperationStatusState(string initialContext = "Active view  ·  No selection")
        {
            ContextSummary = string.IsNullOrWhiteSpace(initialContext)
                ? "No active view  ·  No selection"
                : initialContext;
            DisplayText = ContextSummary;
        }

        public string ContextSummary { get; private set; }

        public string DisplayText { get; private set; }

        public bool IsNotificationVisible { get; private set; }

        public bool IsError { get; private set; }

        public bool IsDismissible => IsError;

        public bool ShouldAutoDismiss { get; private set; }

        public void Show(string message, bool isError, bool autoDismiss)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                Dismiss();
                return;
            }

            IsNotificationVisible = true;
            IsError = isError;
            ShouldAutoDismiss = autoDismiss && !isError;
            DisplayText = isError ? "Error: " + message : message;
        }

        public void UpdateContext(string contextName, int selectedCount)
        {
            string safeContextName = string.IsNullOrWhiteSpace(contextName) ? "No active view" : contextName;
            string selectionSummary = selectedCount <= 0
                ? "No selection"
                : selectedCount == 1 ? "1 selected" : selectedCount + " selected";
            string nextSummary = safeContextName + "  ·  " + selectionSummary;
            bool contextChanged = !string.Equals(ContextSummary, nextSummary, StringComparison.Ordinal);

            ContextSummary = nextSummary;
            if (!IsNotificationVisible || (contextChanged && !IsError))
                Dismiss();
        }

        public void Dismiss()
        {
            IsNotificationVisible = false;
            IsError = false;
            ShouldAutoDismiss = false;
            DisplayText = ContextSummary;
        }
    }
}
