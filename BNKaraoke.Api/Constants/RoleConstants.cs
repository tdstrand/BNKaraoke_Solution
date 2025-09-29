namespace BNKaraoke.Api.Constants
{
    public static class RoleConstants
    {
        public const string ApplicationManager = "Application Manager";
        public const string EventManager = "Event Manager";
        public const string EventAdministrator = "Event Administrator";
        public const string DjAdministrator = "DJ Administrator";
        public const string KaraokeDj = "Karaoke DJ";
        public const string Singer = "Singer";
        public const string QueueManager = "Queue Manager";
        public const string SongManager = "Song Manager";
        public const string UserManager = "User Manager";

        public static readonly string[] EventManagementRoles =
        {
            EventManager,
            EventAdministrator,
            ApplicationManager
        };

        public static readonly string[] HiddenEventAccessRoles =
        {
            DjAdministrator,
            EventAdministrator,
            EventManager,
            ApplicationManager
        };

        public const string EventManagementRolesCsv =
            EventManager + "," + EventAdministrator + "," + ApplicationManager;

        public const string HiddenEventAccessRolesCsv =
            DjAdministrator + "," + EventAdministrator + "," + EventManager + "," + ApplicationManager;
    }
}
