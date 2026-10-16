namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the game's version number, on its own so the dedicated server can share it (it's
    /// linked into SimsRedux.Server too, which reports it to the website's launcher for the version check).
    ///
    /// EVERY UPDATE: raise Number by 0.1 (and add lines to ReduxVersion.WhatsNew and wwwroot/patchnotes.json).
    /// </summary>
    public static class ReduxBuild
    {
        public const string Number = "4.4";
        public const string Stage = "Beta";
    }
}
