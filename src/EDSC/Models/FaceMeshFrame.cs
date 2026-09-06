using System;

namespace EDSC.Models
{
    /// <summary>
    /// One frame of face mesh line segments for the desktop preview panel, in coordinates
    /// normalised to the source camera frame (0..1 on both axes). Only the lines travel,
    /// never camera pixels, so a frame is a few kilobytes and costs nothing to encode or decode.
    /// </summary>
    public sealed class FaceMeshFrame
    {
        /// <summary>Source frame width in pixels. Only the aspect ratio matters to the viewer.</summary>
        public int Width { get; }

        /// <summary>Source frame height in pixels. Only the aspect ratio matters to the viewer.</summary>
        public int Height { get; }

        /// <summary>Line groups in draw order.</summary>
        public FaceMeshGroup[] Groups { get; }

        public FaceMeshFrame(int width, int height, FaceMeshGroup[] groups)
        {
            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            Width = width > 0 ? width : 4;
            Height = height > 0 ? height : 3;
            Groups = groups;
        }
    }

    /// <summary>
    /// A set of line segments sharing one style.
    /// </summary>
    public sealed class FaceMeshGroup
    {
        /// <summary>What the lines represent; the viewer maps this to a colour.</summary>
        public FaceMeshStyle Style { get; }

        /// <summary>Stroke width in preview pixels at the viewer's reference size.</summary>
        public float LineWidth { get; }

        /// <summary>Segments packed as x1, y1, x2, y2 per segment, normalised 0..1.</summary>
        public float[] Segments { get; }

        public int SegmentCount
        {
            get
            {
                return Segments.Length / 4;
            }
        }

        public FaceMeshGroup(FaceMeshStyle style, float lineWidth, float[] segments)
        {
            if (segments == null)
            {
                throw new ArgumentNullException(nameof(segments));
            }

            Style = style;
            LineWidth = lineWidth > 0 ? lineWidth : 1f;
            Segments = segments;
        }
    }

    /// <summary>
    /// Semantic line styles shared by the phone script and the desktop viewer. The numeric values
    /// are part of the wire format on the /pose socket, so add new entries at the end only.
    /// </summary>
    public enum FaceMeshStyle : byte
    {
        /// <summary>Face outline (jaw / oval).</summary>
        Outline = 0,

        /// <summary>Eyes and brows.</summary>
        Eyes = 1,

        /// <summary>Lips.</summary>
        Lips = 2,

        /// <summary>Dense tessellation drawn faintly behind the outlines.</summary>
        Tessellation = 3,

        /// <summary>Detected face bounding box.</summary>
        FaceBox = 4,

        /// <summary>Nose bridge and base.</summary>
        Nose = 5
    }
}
