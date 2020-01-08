/*
 * Copyright (C) 2016-2018. Filament Games, LLC. All rights reserved.
 * Author:  Alex Beauchesne
 * Date:    7 Jan 2020
 * 
 * File:    AsyncWOrkUnit.cs
 * Purpose: Single unit of work for an async worker.
 */

#if !UNITY_WEBGL
#define SUPPORTS_THREADING
#endif // UNITY_WEBGL

using System;
using System.Collections;
using System.Collections.Generic;

namespace BeauRoutine.Internal
{
    internal sealed class AsyncWorkUnit
    {
        internal enum StepResult : byte
        {
            Incomplete,
            Complete
        }

        private const ushort ActionCompleteFlag = 0x01;
        private const ushort EnumeratorCompleteFlag = 0x02;
        private const ushort AllCompleteFlag = ActionCompleteFlag | EnumeratorCompleteFlag;

        // state
        private ushort m_Status;
        private ushort m_Serial;
        private Action m_Action;
        private IEnumerator m_Enumerator;
        internal AsyncFlags AsyncFlags;

        // misc
        private List<AsyncHandle> m_Nested = new List<AsyncHandle>();
        private Action m_OnStop;

        #if SUPPORTS_THREADING
        private readonly object m_LockContext = new object();
        #endif // SUPPORTS_THREADING

        #region Lifecycle

        /// <summary>
        /// Initializes the work unit with new work.
        /// </summary>
        internal void Initialize(Action inAction, IEnumerator inEnumerator, AsyncFlags inFlags)
        {
            m_Status = AllCompleteFlag;

            m_Action = inAction;
            if (m_Action != null)
                m_Status = (ushort) (m_Status & ~ActionCompleteFlag);

            m_Enumerator = inEnumerator;
            if (m_Enumerator != null)
                m_Status = (ushort) (m_Status & ~EnumeratorCompleteFlag);

            AsyncFlags = inFlags;

            if (m_Serial == ushort.MaxValue)
            {
                m_Serial = 1;
            }
            else
            {
                ++m_Serial;
            }
        }

        /// <summary>
        /// Schedules a callback when the work unit is stopped.
        /// </summary>
        internal void OnStopCallback(ushort inSerial, Action inOnStop)
        {
            #if SUPPORTS_THREADING
            lock(m_LockContext)
            {
                if (m_Serial == inSerial)
                {
                    m_OnStop += inOnStop;
                }
            }
            #else
            if (m_Serial == inSerial)
            {
                m_OnStop += inOnStop;
            }
            #endif // SUPPORTS_THREADING
        }

        /// <summary>
        /// Dispatch any callbacks.
        /// </summary>
        internal void DispatchStop(AsyncDispatcher inDispatcher)
        {
            #if SUPPORTS_THREADING
            lock(m_LockContext)
            {
                if (m_OnStop != null)
                {
                    inDispatcher.EnqueueInvoke(m_OnStop);
                    m_OnStop = null;
                }
            }
            #else
            if (m_OnStop != null)
            {
                inDispatcher.EnqueueInvoke(m_OnStop);
                m_OnStop = null;
            }
            #endif // SUPPORTS_THREADING
        }

        /// <summary>
        /// Clears the work unit's contents.
        /// </summary>
        internal void Clear()
        {
            m_Status = AllCompleteFlag;
            m_Action = null;
            DisposeUtils.DisposeObject(ref m_Enumerator);
            AsyncFlags = 0;
            m_Nested.Clear();
            m_OnStop = null;
        }

        #endregion // Lifecycle

        #region Status

        /// <summary>
        /// Returns if this work unit is running.
        /// </summary>
        internal bool IsRunning(ushort inSerial)
        {
            return m_Serial == inSerial && m_Status != AllCompleteFlag;
        }

        /// <summary>
        /// Returns a handle to the work unit.
        /// </summary>
        internal AsyncHandle GetHandle()
        {
            return new AsyncHandle(this, m_Serial);
        }

        #endregion // Status

        #region Step

        /// <summary>
        /// Performs a step (not thread-safe).
        /// Returns if work remains.
        /// </summary>
        internal StepResult Step()
        {
            if ((m_Status & ActionCompleteFlag) == 0)
            {
                m_Action();
                m_Action = null;
                m_Status |= ActionCompleteFlag;
            }
            else if ((m_Status & EnumeratorCompleteFlag) == 0)
            {
                if (!m_Enumerator.MoveNext())
                {
                    DisposeUtils.DisposeObject(ref m_Enumerator);
                    m_Status |= EnumeratorCompleteFlag;
                }
            }

            return m_Status != AllCompleteFlag ? StepResult.Incomplete : StepResult.Complete;
        }

        #if SUPPORTS_THREADING

        /// <summary>
        /// Performs a step (thread-safe).
        /// Returns if work remains.
        /// </summary>
        internal StepResult ThreadedStep()
        {
            lock(m_LockContext)
            {
                return Step();
            }
        }

        #endif // SUPPORTS_THREADING

        #endregion // Step

        #region Cancel

        // Cancels all future steps
        private void Cancel()
        {
            m_Status = AllCompleteFlag;
            for (int i = m_Nested.Count - 1; i >= 0; --i)
            {
                m_Nested[i].Cancel();
            }
            m_Nested.Clear();
        }

        /// <summary>
        /// Attempts to cancel work.
        /// </summary>
        internal void TryCancel(ushort inSerial)
        {
            #if SUPPORTS_THREADING
            lock(m_LockContext)
            {
                if (m_Serial == inSerial)
                {
                    Cancel();
                }
            }
            #else
            if (m_Serial == inSerial)
            {
                Cancel();
            }
            #endif // SUPPORTS_THREADING
        }

        #endregion // Cancel
    }
}