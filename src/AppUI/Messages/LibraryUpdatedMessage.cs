using CommunityToolkit.Mvvm.Messaging.Messages;

namespace AppUI.Messages
{
    public sealed class LibraryUpdatedMessage : ValueChangedMessage<bool>
    {
        public LibraryUpdatedMessage() : base(true) { }
    }
}