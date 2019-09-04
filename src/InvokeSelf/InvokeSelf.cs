using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Threading.Timer;

namespace InvokeSelf
{
    public static class ControlExtensions
    {
        private static bool _startWaitingMainForm;
        private static bool _finishWaitingMainForm;
        private static readonly object StartWaitingMainFormSync = new object();
        private static readonly object WaitMainFormQueueSync = new object();
        private static readonly Queue<Action> WaitMainFormQueue = new Queue<Action>();
        private static Timer _waitingMainFormTimer;

        private static bool _initialized;
        private static Control _mainForm;
        private static int _uiThreadId;
        private static Action<string> _errorMsg;
        private static Action<Exception, string> _errorMsgExc;
        private static Action<string> _traceMsg;

        public static void Init(Control mainForm, int uiThreadId, Action<string> errorMsg = null, Action<Exception, string> errorMsgExc = null, Action<string> traceMsg = null)
        {
            _mainForm = mainForm;
            _uiThreadId = uiThreadId;
            _errorMsg = errorMsg;
            _errorMsgExc = errorMsgExc;
            _traceMsg = traceMsg;
        }

        private static Task Delay(Control control)
        {
            var tcs = new TaskCompletionSource<object>();
            _waitingMainFormTimer = new Timer(o =>
            {
                if (control.IsHandleCreated)
                {
                    tcs.TrySetResult(null);
                    _waitingMainFormTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _waitingMainFormTimer = null;
                }
            });
            _waitingMainFormTimer.Change(0, 20);
            return tcs.Task;
        }

        private static void WaitMainForm(Action action, string stack)
        {
            lock (WaitMainFormQueueSync)
            {
                WaitMainFormQueue.Enqueue(() => WaitMainFormInvoke(action, stack));
            }

            if (_mainForm != null && _mainForm.IsHandleCreated)
                InvokeActionBefore();

            lock (StartWaitingMainFormSync)
            {
                if (_mainForm != null && !_startWaitingMainForm)
                {
                    _startWaitingMainForm = true;
                    Delay(_mainForm).ContinueWith(t => InvokeActionBefore());
                }
            }
        }

        private static void InvokeActionBefore()
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
                InvokeActionBeforeUiThread();
            else
                _mainForm?.Invoke((MethodInvoker)delegate { InvokeActionBeforeUiThread(); });
        }

        private static void InvokeActionBeforeUiThread()
        {
            lock (WaitMainFormQueueSync)
            {
                if (_finishWaitingMainForm) return;
                try
                {
                    while (WaitMainFormQueue.Count != 0)
                    {
                        var actionBefore = WaitMainFormQueue.Dequeue();
                        actionBefore();
                    }
                }
                catch (Exception ex)
                {
                    _errorMsgExc?.Invoke(ex, "Error in InvokeActionBefore");
                }
                finally
                {
                    _finishWaitingMainForm = true;
                }
            }
        }

        private static void WaitMainFormInvoke(Action action, string stack)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _errorMsgExc?.Invoke(ex, "Invoke after waiting handle fail!" + Environment.NewLine + stack);
            }
        }

        public static async Task InvokeSelfAsync(this Control control, Func<Task> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                await action();
                return;
            }
            if (!ReferenceEquals(_mainForm, null) && !_mainForm.IsDisposed)
                await InvokeMainFormAsync(action);
            else
            {
                if (control == null)
                {
                    _errorMsg?.Invoke("InvokeSelfAsync control is null!" + Environment.NewLine + Environment.StackTrace);
                    return;
                }
                if (!control.IsHandleCreated)
                {
                    var stack = Environment.StackTrace;
                    _errorMsg?.Invoke("InvokeSelfAsync control has not handle!" + Environment.NewLine + stack);
                    await Task.Run(async () =>
                    {
                        while (!_mainForm.IsHandleCreated)
                            await Task.Delay(20);
                    }).ContinueWith(async t =>
                    {
                        Task task1 = null;
                        _mainForm.Invoke((MethodInvoker)delegate { task1 = action(); });
                        if (task1 != null) await task1;
                    });
                    return;
                }

                Task task = null;
                control.Invoke((MethodInvoker)delegate { task = action(); });
                if (task != null) await task;
            }
        }

        public static async Task InvokeMainFormAsync(Func<Task> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                await action();
                return;
            }
            if (ReferenceEquals(_mainForm, null))
            {
                _errorMsg?.Invoke("InvokeMainFormAsync MainForm is null!" + Environment.NewLine + Environment.StackTrace);
                return;
            }
            if (!_mainForm.IsHandleCreated)
            {
                var stack = Environment.StackTrace;
                _errorMsg?.Invoke("InvokeMainFormAsync MainForm has not handle!" + Environment.NewLine + stack);
                await Task.Run(async () =>
                {
                    while (!_mainForm.IsHandleCreated)
                        await Task.Delay(20);
                }).ContinueWith(async t =>
                {
                    Task task1 = null;
                    _mainForm.Invoke((MethodInvoker)delegate { task1 = action(); });
                    if (task1 != null) await task1;
                });
                return;
            }
            if (!_finishWaitingMainForm)
                InvokeActionBefore();
            Task task = null;
            _mainForm.Invoke((MethodInvoker)delegate { task = action(); });
            if (task != null) await task;
        }

        public static void InvokeMainForm(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                action();
                return;
            }
            if (ReferenceEquals(_mainForm, null))
            {
                _errorMsg?.Invoke("InvokeSelf MainForm is null!" + Environment.NewLine + Environment.StackTrace);
                return;
            }
            if (!_mainForm.IsHandleCreated)
            {
                var stack = Environment.StackTrace;
                _traceMsg?.Invoke("InvokeSelf MainForm has not handle!" + Environment.NewLine + stack);
                WaitMainForm(action, stack);
                return;
            }
            if (!_finishWaitingMainForm)
                InvokeActionBefore();
            _mainForm.Invoke(action);
        }

        public static void InvokeSelf(this Control control, Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                action();
                return;
            }
            if (!ReferenceEquals(_mainForm, null) && !_mainForm.IsDisposed)
                InvokeMainForm(action);
            else
            {
                if (control == null)
                {
                    _errorMsg?.Invoke("InvokeSelf control is null!" + Environment.NewLine + Environment.StackTrace);
                    return;
                }
                if (!control.IsHandleCreated)
                {
                    var stack = Environment.StackTrace;
                    _errorMsg?.Invoke("InvokeSelf control has not handle! MainForm is null!" + Environment.NewLine + stack);
                    WaitMainForm(action, stack);
                    return;
                }
                control.Invoke(action);
            }
        }

        public static T InvokeMainForm<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                return action();
            }
            if (ReferenceEquals(_mainForm, null))
            {
                _errorMsg?.Invoke("InvokeMainForm MainForm is null!" + Environment.NewLine + Environment.StackTrace);
                return default(T);
            }
            if (!_mainForm.IsHandleCreated)
            {
                var stack = Environment.StackTrace;
                _errorMsg?.Invoke("InvokeMainForm MainForm has not handle!" + Environment.NewLine + stack);
                return default(T);
            }
            if (!_finishWaitingMainForm)
                InvokeActionBefore();
            T res = default(T);
            _mainForm.Invoke((MethodInvoker)delegate { res = action(); });
            return res;
        }

        public static T InvokeSelf<T>(this Control control, Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
            {
                return action();
            }
            if (!ReferenceEquals(_mainForm, null) && _mainForm.IsHandleCreated && !_mainForm.IsDisposed)
                return InvokeMainForm(action);
            else
            {
                if (control == null)
                {
                    _errorMsg?.Invoke("InvokeSelfObj control is null!" + Environment.NewLine + Environment.StackTrace);
                    return default(T);
                }
                if (!control.IsHandleCreated)
                {
                    var stack = Environment.StackTrace;
                    _errorMsg?.Invoke("InvokeSelfObj control has not handle!" + Environment.NewLine + stack);
                    return default(T);
                }
                T res = default(T);
                control.Invoke((MethodInvoker)delegate { res = action(); });
                return res;
            }
        }
    }
}