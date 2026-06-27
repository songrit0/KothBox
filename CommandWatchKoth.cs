using Rocket.API;
using Rocket.Unturned.Player;
using System.Collections.Generic;

namespace KothBox
{
    public class CommandWatchKoth : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "watchkoth";
        public string Help => "Watch the KOTH event as a spectator.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            KothBox.Instance?.WatchKoth((UnturnedPlayer)caller);
        }
    }
}
