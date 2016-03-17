﻿using Exrin.Abstraction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Exrin.Framework
{
    public static partial class Process
    {
        public static IRelayCommand ViewModelExecute(this IExecution sender, IViewModelExecute execute, [CallerMemberName] string name = "")
        {
            
            return new RelayCommand(async (parameter) =>
            {
                await ViewModelExecute(sender,
                                        operations: execute.Operations,
                                        handleTimeout: sender.HandleTimeout,
                                        handleUnhandledException: sender.HandleUnhandledException,
                                        insights: sender.Insights,
                                        notifyActivityFinished: sender.NotifyActivityFinished,
                                        notifyOfActivity: sender.NotifyOfActivity,
                                        timeoutMilliseconds: execute.TimeoutMilliseconds,
                                        name: name,
                                        parameter: parameter);
            });
        }

        private readonly static Dictionary<object, bool> _status = new Dictionary<object, bool>();

        private static async Task ViewModelExecute(IExecution sender,
                 List<IOperation> operations,
                 Func<Task> notifyOfActivity = null,
                 Func<Task> notifyActivityFinished = null,
                 Func<Exception, Task<bool>> handleUnhandledException = null,
                 Func<Task> handleTimeout = null,
                 int timeoutMilliseconds = 0,
                 IApplicationInsights insights = null,
                 string name = "",
                 object parameter = null
                 )
        {
            // If current executing, ignore the latest request
            lock (sender)
            {
                if (!_status.ContainsKey(sender))
                    _status.Add(sender, true);
                else if (_status[sender])
                    return;
                else
                    _status[sender] = true;
            }

            if (sender == null)
                throw new Exception($"The IExecution sender can not be null");

            sender.Result = null;

            // Background thread
            Task insight = Task.Run(() =>
            {
                try
                {
                    if (insights != null)
                        insights.TrackEvent(name, $"User activated {name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"insights.TrackEvent({name}) {ex.Message}");
                }
            });

            List<Func<IResult, Task>> rollbacks = new List<Func<IResult, Task>>();
            bool transactionRunning = false;

            // Setup Cancellation of Tasks if long running
            var task = new CancellationTokenSource();

            if (timeoutMilliseconds > 0)
            {
                if (handleTimeout != null)
                    task.Token.Register(async () => { await handleTimeout(); });
                else if (handleUnhandledException != null)
                    task.Token.Register(async () => { await handleUnhandledException(new TimeoutException()); });
                else
                    throw new Exception($"You must specify either {nameof(handleTimeout)} or {nameof(handleUnhandledException)} to handle a timeout.");

                task.CancelAfter(timeoutMilliseconds);
            }

            IResult result = null;

            try
            {
                if (notifyOfActivity == null)
                    throw new Exception($"{nameof(notifyOfActivity)} is null: You must notify the user that something is happening");

                await notifyOfActivity();

                // Transaction Block
                transactionRunning = true;

                foreach (var operation in operations)
                {
                    rollbacks.Add(operation.Rollback);

                    if (result == null)
                        result = new Result(parameter); // Ensures always an instance

                    if (operation.Function != null)
                        await Task.Run(async () => { await operation.Function(result); }, task.Token); // Background Thread

                    if (!operation.ChainedRollback)
                        rollbacks.Remove(operation.Rollback);
                }

                rollbacks.Clear();
                transactionRunning = false;
                // End of Transaction Block


            }
            catch (Exception e)
            {
                if (handleUnhandledException == null)
                    throw;

                var handled = await handleUnhandledException(e);
                if (!handled)
                    throw;
            }
            finally
            {
                try
                {
                    if (transactionRunning)
                    {
                        rollbacks.Reverse(); // Do rollbacks in reverse order
                        foreach (var rollback in rollbacks)
                        {
                            if (result == null)
                                result = new Result(parameter);

                            await rollback(result);
                        }
                    }

                    // Set final result
                    sender.Result = result;

                    // Handle the result
                    await Task.Run(async () => await sender.HandleResult(sender.Result)); //TODO: why am I passing this in again?
                }
                finally
                {
                    if (notifyActivityFinished == null)
                        throw new Exception($"{nameof(notifyActivityFinished)} is null: You need to specify what happens when the operations finish");

                    try
                    {
                        await notifyActivityFinished();
                    }
                    catch (Exception e)
                    {
                        var handled = await handleUnhandledException(e);
                        if (!handled)
                            throw;
                    }
                    finally
                    {
                        _status.Remove(sender);
                    }
                }

            }
        }

    }
}
