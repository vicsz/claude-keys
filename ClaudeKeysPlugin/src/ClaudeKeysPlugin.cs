namespace Loupedeck.ClaudeKeysPlugin
{
    using System;

    // This class contains the plugin-level logic of the Loupedeck plugin.

    public class ClaudeKeysPlugin : Plugin
    {
        // Gets a value indicating whether this is an API-only plugin.
        public override Boolean UsesApplicationApiOnly => true;

        // Gets a value indicating whether this is a Universal plugin or an Application plugin.
        public override Boolean HasNoApplication => true;

        // Gets the live Claude session data shared by all keys.
        public ClaudeSessionStore Sessions { get; } = new ClaudeSessionStore();

        // Initializes a new instance of the plugin class.
        public ClaudeKeysPlugin()
        {
            // Initialize the plugin log.
            PluginLog.Init(this.Log);

            // Initialize the plugin resources.
            PluginResources.Init(this.Assembly);
        }

        // This method is called when the plugin is loaded.
        public override void Load() => this.Sessions.Start();

        // This method is called when the plugin is unloaded.
        public override void Unload() => this.Sessions.Dispose();
    }
}
