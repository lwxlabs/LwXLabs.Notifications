using System;
using Android.App;
using Android.Content.PM;
using Android.OS;

namespace LwXLabs.Notifications.Firebase.Android;

public static class FirebaseAndroidNotificationPermission
{
    private const string PostNotifications = "android.permission.POST_NOTIFICATIONS";

    public static bool IsGranted(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu || activity.CheckSelfPermission(PostNotifications) == Permission.Granted;
    }

    /// <summary>Returns true if already granted. On Android 13+, otherwise starts the system permission request; handle its result in the activity.</summary>
    public static bool RequestIfNeeded(Activity activity, int requestCode)
    {
        if (IsGranted(activity)) return true;
        activity.RequestPermissions(new[] { PostNotifications }, requestCode);
        return false;
    }
}
