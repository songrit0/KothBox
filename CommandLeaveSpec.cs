using Rocket.API;
using Rocket.Unturned.Player;
using System.Collections.Generic;

namespace KothBox
{
    public class CommandLeaveSpec : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "leavespec";
        public string Help => "Leave spectator mode and return to your original position.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            KothBox.Instance?.LeaveSpec((UnturnedPlayer)caller);
        }
    }
}
