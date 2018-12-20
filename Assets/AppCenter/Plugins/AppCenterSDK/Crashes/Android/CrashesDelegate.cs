﻿// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT license.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace Microsoft.AppCenter.Unity.Crashes.Internal
{
    public class CrashesDelegate : AndroidJavaProxy
    {
        public static event Crashes.SendingErrorReportHandler SendingErrorReport;
        public static event Crashes.SentErrorReportHandler SentErrorReport;
        public static event Crashes.FailedToSendErrorReportHandler FailedToSendErrorReport;

        private static readonly CrashesDelegate instance = new CrashesDelegate();

        private Crashes.UserConfirmationHandler shouldAwaitUserConfirmationHandler = null;

        private CrashesDelegate() : base("com.microsoft.appcenter.crashes.CrashesListener")
        {
        }

        public static void SetDelegate()
        {
            var crashes = new AndroidJavaClass("com.microsoft.appcenter.crashes.Crashes");
            crashes.CallStatic("setListener", instance);
        }

        //TODO bind error report; implement these
        public void onBeforeSending(AndroidJavaObject report)
        {
            var handlers = SendingErrorReport;
            if (handlers != null)
            {
                var errorReport = ErrorReportConverter.Convert(report);
                handlers.Invoke(errorReport);
            }
        }

        public void onSendingFailed(AndroidJavaObject report, AndroidJavaObject exception)
        {
            var handlers = FailedToSendErrorReport;
            if (handlers != null)
            {
                var errorReport = ErrorReportConverter.Convert(report);
                handlers.Invoke(errorReport);
            }
        }

        public void onSendingSucceeded(AndroidJavaObject report)
        {
            var handlers = SentErrorReport;
            if (handlers != null)
            {
                var errorReport = ErrorReportConverter.Convert(report);
                handlers.Invoke(errorReport);
            }
        }

        public bool shouldProcess(AndroidJavaObject report)
        {
            return true;
        }

        public bool shouldAwaitUserConfirmation()
        {
            if (instance.shouldAwaitUserConfirmationHandler != null)
            {
                return instance.shouldAwaitUserConfirmationHandler.Invoke();
            }

            return false;
        }

        public AndroidJavaObject getErrorAttachments(AndroidJavaObject report)
        {
            return null;
        }

        public static void SetShouldAwaitUserConfirmationHandler(Crashes.UserConfirmationHandler handler)
        {
            instance.shouldAwaitUserConfirmationHandler = handler;
        }

        public static void SetShouldProcessErrorReportHandler(Crashes.ShouldProcessErrorReportHandler handler)
        {
        }

        public static void SetGetErrorAttachmentsHandler(Crashes.GetErrorAttachmentsHandler handler)
        {
        }
    }
}
#endif
