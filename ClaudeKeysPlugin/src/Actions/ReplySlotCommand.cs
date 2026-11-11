namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // The two keys with no normal job. They stay blank until the reply page opens.

    public class ReplySlotCommand : PluginDynamicCommand
    {
        private static readonly Int32[] Slots = { 4, 5 };

        public ReplySlotCommand()
            : base()
        {
            this.IsWidget = true;

            for (var i = 0; i < Slots.Length; i++)
            {
                this.AddParameter(Slots[i].ToString(), $"Reply key {i + 1}", "Reply keys");
            }
        }

        protected override Boolean OnLoad()
        {
            ReplyMode.Changed += this.RedrawAll;
            return true;
        }

        protected override Boolean OnUnload()
        {
            ReplyMode.Changed -= this.RedrawAll;
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            if (Int32.TryParse(actionParameter, out var slot))
            {
                ReplyMode.TryPress(slot, this.Plugin.ClientApplication);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            ReplyMode.IsActive && Int32.TryParse(actionParameter, out var slot) ? ReplyMode.Draw(slot, imageSize) : KeyArt.Empty(imageSize);

        private void RedrawAll()
        {
            foreach (var slot in Slots)
            {
                this.ActionImageChanged(slot.ToString());
            }
        }
    }
}
