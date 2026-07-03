namespace ARMatteCapture.Webcam
{
    /// <summary>
    /// Identifies the purpose of a webcam source in the dual-camera pipeline.
    /// </summary>
    public enum WebcamRole
    {
        /// <summary>Portrait webcam for RVM background removal + main lantern marker tracking.</summary>
        Portrait,

        /// <summary>Paper scan webcam for detecting 4-corner ArUco markers and capturing paper region.</summary>
        PaperScan
    }
}
