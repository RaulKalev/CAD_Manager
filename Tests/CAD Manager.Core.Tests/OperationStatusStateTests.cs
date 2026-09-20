using CAD_Manager.Core;
using Xunit;

namespace CAD_Manager.Core.Tests
{
    public sealed class OperationStatusStateTests
    {
        [Fact]
        public void InformationalMessageReturnsToContextWhenContextChanges()
        {
            OperationStatusState state = new OperationStatusState();
            state.UpdateContext("Level 1", 2);
            state.Show("Updated visibility for 2 items.", false, true);

            state.UpdateContext("Level 2", 0);

            Assert.False(state.IsNotificationVisible);
            Assert.Equal("Level 2  ·  No selection", state.DisplayText);
        }

        [Fact]
        public void ErrorPersistsAcrossContextChangesUntilDismissed()
        {
            OperationStatusState state = new OperationStatusState();
            state.UpdateContext("Level 1", 1);
            state.Show("The view changed.", true, true);

            state.UpdateContext("Level 2", 0);

            Assert.True(state.IsNotificationVisible);
            Assert.True(state.IsError);
            Assert.False(state.ShouldAutoDismiss);
            Assert.Equal("Error: The view changed.", state.DisplayText);

            state.Dismiss();
            Assert.Equal("Level 2  ·  No selection", state.DisplayText);
        }
    }
}
