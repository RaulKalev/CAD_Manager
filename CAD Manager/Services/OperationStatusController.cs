using System;
using System.Windows.Threading;
using CAD_Manager.Core;

namespace CAD_Manager.Services
{
    /// <summary>
    /// Owns the transient-status timer and publishes render-ready state to the view.
    /// </summary>
    public sealed class OperationStatusController : IDisposable
    {
        private readonly OperationStatusState _state;
        private readonly Action<OperationStatusState> _render;
        private DispatcherTimer _timer;

        public OperationStatusController(Action<OperationStatusState> render)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _state = new OperationStatusState();
        }

        public void Show(string message, bool isError, bool autoDismiss = false)
        {
            StopTimer();
            _state.Show(message, isError, autoDismiss);
            _render(_state);

            if (_state.ShouldAutoDismiss)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }
        }

        public void UpdateContext(string contextName, int selectedCount)
        {
            bool notificationWasVisible = _state.IsNotificationVisible;
            _state.UpdateContext(contextName, selectedCount);
            if (notificationWasVisible && !_state.IsNotificationVisible)
                StopTimer();
            _render(_state);
        }

        public void Dismiss()
        {
            StopTimer();
            _state.Dismiss();
            _render(_state);
        }

        public void Dispose()
        {
            StopTimer();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Dismiss();
        }

        private void StopTimer()
        {
            if (_timer == null)
                return;

            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }
    }
}
