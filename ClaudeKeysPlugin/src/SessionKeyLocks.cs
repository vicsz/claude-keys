namespace Loupedeck.ClaudeKeysPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    // Remembers which session is locked to each session key, across restarts.

    public sealed class SessionKeyLocks
    {
        private readonly String _path;
        private readonly String[] _ids;

        public SessionKeyLocks(String dataDirectory, Int32 keyCount)
        {
            Directory.CreateDirectory(dataDirectory);
            this._path = Path.Combine(dataDirectory, "session-keys.json");
            this._ids = new String[keyCount];
            this.IsNew = !File.Exists(this._path);

            if (!this.IsNew)
            {
                this.Read();
            }
        }

        // True until the keys have been saved once, so they can start from Claude's pins.
        public Boolean IsNew { get; private set; }

        public String Get(Int32 key) => this._ids[key];

        // A session can only be on one key, so locking it here clears it anywhere else.
        public void Lock(Int32 key, String id)
        {
            for (var i = 0; i < this._ids.Length; i++)
            {
                if (this._ids[i] == id)
                {
                    this._ids[i] = null;
                }
            }

            this._ids[key] = id;
            this.Save();
        }

        public void Clear(Int32 key)
        {
            this._ids[key] = null;
            this.Save();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(this._path, JsonSerializer.Serialize(this._ids));
                this.IsNew = false;
            }
            catch (IOException ex)
            {
                PluginLog.Warning(ex, "Could not save the session keys");
            }
        }

        private void Read()
        {
            try
            {
                var saved = JsonSerializer.Deserialize<String[]>(File.ReadAllText(this._path)) ?? Array.Empty<String>();

                foreach (var (id, key) in saved.Take(this._ids.Length).Select((id, key) => (id, key)))
                {
                    this._ids[key] = id;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                PluginLog.Warning(ex, "Could not read the session keys");
            }
        }
    }
}
