namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>
    /// Line icons for the widgets: path data in a 24x24 box, drawn as round-capped strokes of <see cref="Stroke"/> units
    /// (WpfRenderer turns them into filled shapes once and caches them). Kept simple: they're drawn at about 15 px.
    /// </summary>
    public static class Icons
    {
        public const double Stroke = 2.8;

        /// <summary>A road in perspective with a dashed centre line: the track (track temperature).</summary>
        public const string Track = "M4.5,21.5 L9.5,2.5 M19.5,21.5 L14.5,2.5 M12,3.5 L12,6.5 M12,10.5 L12,13.5 M12,17.5 L12,21";

        /// <summary>Gusts of wind: the air (air temperature).</summary>
        public const string Air = "M2.5,8 L13,8 A3.2,3.2 0 1 0 10,4.2 M2.5,13 L18,13 A3.2,3.2 0 1 1 15,16.8 M2.5,18 L9,18";
    }
}
