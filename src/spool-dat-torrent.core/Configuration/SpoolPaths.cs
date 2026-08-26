namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// Default container-side destination roots.
    ///
    /// The web UI runs inside a Docker container in production, where the final output lives
    /// under two fixed container-side mount points whose RHS must match these defaults:
    ///
    ///   - /mnt/pool/media/bin/games/staging:/staging-dir
    ///   - /mnt/pool/media/games/roms:/library-dir
    ///
    /// These are only DEFAULT values. The actual roots live in config.json
    /// (GlobalSpoolSettings.DefaultSpoolingTarget for staging, GlobalSpoolSettings.LibraryDir
    /// for the library) so they can be overridden for local Windows development. The CLI
    /// (Windows) does not use these defaults — it passes full absolute Windows paths directly.
    /// </summary>
    public static class SpoolPaths
    {
        /// <summary>Default staging root: files awaiting further processing.</summary>
        public const string DefaultStagingDir = "/staging-dir";

        /// <summary>Default library root: files needing no further processing.</summary>
        public const string DefaultLibraryDir = "/library-dir";
    }
}
