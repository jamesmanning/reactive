﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reactive.Disposables;
using System.Threading;
using System.Windows.Forms;

namespace System.Reactive.Concurrency
{
    /// <summary>
    /// Obsolete. The <c>System.Reactive.Integration.WindowsForms</c> NuGet package defines a
    /// <c>ControlScheduler</c> class in the <c>System.Reactive.Integration.WindowsForms</c>
    /// namespace that replaces this class.
    /// </summary>
    /// <remarks>
    /// This will eventually be removed because all UI-framework-specific functionality is being
    /// removed from <c>System.Reactive</c>. This is necessary to fix problems in which
    /// <c>System.Reactive</c> causes applications to end up with dependencies on Windows Forms and
    /// WPF whether they want them or not.
    /// </remarks>
    [Obsolete("Use System.Reactive.Integration.WindowsForms.ControlScheduler in the System.Reactive.Integration.WindowsForms package instead", error: false)]
    public class ControlScheduler : LocalScheduler, ISchedulerPeriodic
    {
        private readonly Control _control;

        /// <summary>
        /// Constructs a ControlScheduler that schedules units of work on the message loop associated with the specified Windows Forms control.
        /// </summary>
        /// <param name="control">Windows Forms control to get the message loop from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
        /// <remarks>
        /// This scheduler type is typically used indirectly through the <see cref="Linq.ControlObservable.ObserveOn{TSource}"/> and <see cref="Linq.ControlObservable.SubscribeOn{TSource}"/> method overloads that take a Windows Forms control.
        /// </remarks>
        public ControlScheduler(Control control)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
        }

        /// <summary>
        /// Gets the control associated with the ControlScheduler.
        /// </summary>
        public Control Control => _control;

        /// <summary>
        /// Schedules an action to be executed on the message loop associated with the control.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public override IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_control.IsDisposed)
            {
                return Disposable.Empty;
            }

            var d = new SingleAssignmentDisposable();

            _control.BeginInvoke(new Action(() =>
            {
                if (!_control.IsDisposed && !d.IsDisposed)
                {
                    d.Disposable = action(this, state);
                }
            }));

            return d;
        }

        /// <summary>
        /// Schedules an action to be executed after dueTime on the message loop associated with the control, using a Windows Forms Timer object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <param name="dueTime">Relative time after which to execute the action.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public override IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var dt = Scheduler.Normalize(dueTime);
            if (dt.Ticks == 0)
            {
                return Schedule(state, action);
            }

            var createTimer = new Func<IScheduler, TState, IDisposable>((scheduler1, state1) =>
            {
                var d = new MultipleAssignmentDisposable();

                var timer = new System.Windows.Forms.Timer();

                timer.Tick += (s, e) =>
                {
                    var t = Interlocked.Exchange(ref timer, null);
                    if (t != null)
                    {
                        try
                        {
                            if (!_control.IsDisposed && !d.IsDisposed)
                            {
                                d.Disposable = action(scheduler1, state1);
                            }
                        }
                        finally
                        {
                            t.Stop();
                            action = static (s, t) => Disposable.Empty;
                        }
                    }
                };

                timer.Interval = (int)dt.TotalMilliseconds;
                timer.Start();

                d.Disposable = Disposable.Create(() =>
                {
                    var t = Interlocked.Exchange(ref timer, null);
                    if (t != null)
                    {
                        t.Stop();
                        action = static (s, t) => Disposable.Empty;
                    }
                });

                return d;
            });

            //
            // This check is critical. When creating and enabling a Timer object on another thread than
            // the UI thread, it won't fire.
            //
            if (_control.InvokeRequired)
            {
                return Schedule(state, createTimer);
            }
            else
            {
                return createTimer(this, state);
            }
        }

        /// <summary>
        /// Schedules a periodic piece of work on the message loop associated with the control, using a Windows Forms Timer object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">Initial state passed to the action upon the first iteration.</param>
        /// <param name="period">Period for running the work periodically.</param>
        /// <param name="action">Action to be executed, potentially updating the state.</param>
        /// <returns>The disposable object used to cancel the scheduled recurring action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is less than one millisecond.</exception>
        public IDisposable SchedulePeriodic<TState>(TState state, TimeSpan period, Func<TState, TState> action)
        {
            //
            // Threshold derived from Interval property setter in ndp\fx\src\winforms\managed\system\winforms\Timer.cs.
            //
            if (period.TotalMilliseconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var createTimer = new Func<IScheduler, TState, IDisposable>((scheduler1, state1) =>
            {
                var timer = new System.Windows.Forms.Timer();

                timer.Tick += (s, e) =>
                {
                    if (!_control.IsDisposed)
                    {
                        state1 = action(state1);
                    }
                };

                timer.Interval = (int)period.TotalMilliseconds;
                timer.Start();

                return Disposable.Create(() =>
                {
                    var t = Interlocked.Exchange(ref timer, null);
                    if (t != null)
                    {
                        t.Stop();
                        action = static _ => _;
                    }
                });
            });

            //
            // This check is critical. When creating and enabling a Timer object on another thread than
            // the UI thread, it won't fire.
            //
            if (_control.InvokeRequired)
            {
                return Schedule(state, createTimer);
            }
            else
            {
                return createTimer(this, state);
            }
        }
    }
}
