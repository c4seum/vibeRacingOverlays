namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>
    /// Line icons for the widgets: path data in a 24x24 box, drawn as round-capped strokes of <see cref="Stroke"/> units
    /// (WpfRenderer turns them into filled shapes once and caches them). Kept simple: they're drawn at about 15 px.
    /// Paths starting with <see cref="FillPrefix"/> are filled shapes instead of strokes (flags, the meatball).
    /// </summary>
    public static class Icons
    {
        public const double Stroke = 2.8;
        public const string FillPrefix = "fill:";

        /// <summary>A road in perspective with a dashed centre line: the track (track temperature).</summary>
        public const string Track = "M4.5,21.5 L9.5,2.5 M19.5,21.5 L14.5,2.5 M12,3.5 L12,6.5 M12,10.5 L12,13.5 M12,17.5 L12,21";

        /// <summary>Gusts of wind: the air (air temperature).</summary>
        public const string Air = "M2.5,8 L13,8 A3.2,3.2 0 1 0 10,4.2 M2.5,13 L18,13 A3.2,3.2 0 1 1 15,16.8 M2.5,18 L9,18";

        /// <summary>A drop: humidity.</summary>
        public const string Humidity = "M12,3 C12,3 5.5,10.5 5.5,15 A6.5,6.5 0 0 0 18.5,15 C18.5,10.5 12,3 12,3 Z";

        /// <summary>A flag on a pole (filled), for the black and furled black flags.</summary>
        public const string Flag = "fill:M3.5,1.5 L6.5,1.5 L6.5,22.5 L3.5,22.5 Z M6.5,2.5 C10,0.8 13,4.2 16.5,2.6 C18.4,1.8 20.5,2 20.5,2 L20.5,14 C18.5,15 15.5,12.8 12.3,14.2 C9.8,15.2 8.3,14.4 6.5,15 Z";

        /// <summary>A filled disc: the meatball (repair) flag.</summary>
        public const string Ball = "fill:M12,1.5 A10.5,10.5 0 1 1 11.99,1.5 Z";
    }
}
