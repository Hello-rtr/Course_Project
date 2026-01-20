namespace Server_My_Messenger.Models
{
    public static class WebSocketMessageTypes
    {
        public const string TEXT_MESSAGE = "TEXT_MESSAGE";
        public const string SELECT_CHAT = "SELECT_CHAT";
        public const string GET_CHATS = "GET_CHATS";
        public const string GET_HISTORY = "GET_HISTORY";
        public const string GET_USERS = "GET_USERS";
        public const string AUTH = "AUTH";
        public const string CREATE_CHAT = "CREATE_CHAT";
        public const string CREATE_PRIVATE_CHAT = "CREATE_PRIVATE_CHAT";
        public const string JOIN_CHAT = "JOIN_CHAT";
        public const string LEAVE_CHAT = "LEAVE_CHAT";
        public const string UPDATE_PROFILE = "UPDATE_PROFILE";
        public const string MARK_AS_READ = "MARK_AS_READ";
        public const string GET_CHAT_USERS = "GET_CHAT_USERS";
        public const string UPDATE_USER_ROLE = "UPDATE_USER_ROLE";
        public const string UPLOAD_AVATAR = "UPLOAD_AVATAR";
        public const string UPDATE_STATUS = "UPDATE_STATUS";
        public const string MARK_CHAT_AS_READ = "MARK_CHAT_AS_READ";
        public const string SEARCH_CHATS = "SEARCH_CHATS";
        public const string SEARCH_USERS = "SEARCH_USERS";
        public const string GLOBAL_SEARCH = "GLOBAL_SEARCH";
        public const string NOTIFICATION = "NOTIFICATION";
        public const string MARK_MULTIPLE_READ = "MARK_MULTIPLE_READ";
        public const string GET_UNREAD_SUMMARY = "GET_UNREAD_SUMMARY";
        public const string MARK_MULTIPLE_MESSAGES_READ = "MARK_MULTIPLE_MESSAGES_READ";
        public const string CREATE_CHAT_WITH_USER = "CREATE_CHAT_WITH_USER";
        public const string CREATE_CHAT_AND_INVITE = "CREATE_CHAT_AND_INVITE";
    }

    public static class ResponseMessageTypes
    {
        public const string USER_STATUS_CHANGE = "USER_STATUS_CHANGE";
        public const string SYSTEM_MESSAGE = "SYSTEM_MESSAGE";
        public const string NEW_MESSAGE = "NEW_MESSAGE";
        public const string CHAT_CREATED = "CHAT_CREATED";
        public const string PRIVATE_CHAT_CREATED = "PRIVATE_CHAT_CREATED";
        public const string JOINED_CHAT = "JOINED_CHAT";
        public const string LEFT_CHAT = "LEFT_CHAT";
        public const string PROFILE_UPDATED = "PROFILE_UPDATED";
        public const string AVATAR_UPLOADED = "AVATAR_UPLOADED";
        public const string STATUS_UPDATED = "STATUS_UPDATED";
        public const string MESSAGE_READ = "MESSAGE_READ";
        public const string CHAT_MARKED_AS_READ = "CHAT_MARKED_AS_READ";
        public const string USER_PROFILE_UPDATE = "USER_PROFILE_UPDATE";
        public const string USER_ROLE_UPDATED = "USER_ROLE_UPDATED";
        public const string YOUR_ROLE_UPDATED = "YOUR_ROLE_UPDATED";
        public const string CHAT_SELECTED = "CHAT_SELECTED";
        public const string CHAT_LIST = "CHAT_LIST";
        public const string MESSAGE_HISTORY = "MESSAGE_HISTORY";
        public const string USERS_LIST = "USERS_LIST";
        public const string CHAT_USERS = "CHAT_USERS";
        public const string SEARCH_RESULTS = "SEARCH_RESULTS";
        public const string SEARCH_USERS_RESULTS = "SEARCH_USERS_RESULTS";
        public const string SEARCH_CHATS_RESULTS = "SEARCH_CHATS_RESULTS";
        public const string GLOBAL_SEARCH_RESULTS = "GLOBAL_SEARCH_RESULTS";
        public const string NEW_CHAT_MESSAGE_NOTIFICATION = "NEW_CHAT_MESSAGE_NOTIFICATION";
        public const string MESSAGE_READ_CONFIRMATION = "MESSAGE_READ_CONFIRMATION";
        public const string MESSAGE_READ_BATCH_CONFIRMATION = "MESSAGE_READ_BATCH_CONFIRMATION";
        public const string MULTIPLE_MESSAGES_READ = "MULTIPLE_MESSAGES_READ";
        public const string UNREAD_SUMMARY = "UNREAD_SUMMARY";
        public const string MESSAGES_BATCH_READ = "MESSAGES_BATCH_READ";
        public const string CHAT_CREATED_WITH_USER = "CHAT_CREATED_WITH_USER";
        public const string CHAT_CREATED_AND_INVITED = "CHAT_CREATED_AND_INVITED";
    }
}