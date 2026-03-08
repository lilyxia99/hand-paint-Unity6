using System.Collections.Generic;

namespace FingerPaint
{
    /// <summary>
    /// Serializable manifest tracking all saved finger-paint works.
    /// Stored as gallery_manifest.json in the SavedWorks directory.
    /// </summary>
    [System.Serializable]
    public class GalleryManifest
    {
        public int version = 1;
        public List<GalleryEntry> works = new List<GalleryEntry>();
    }

    /// <summary>
    /// A single saved work entry in the gallery manifest.
    /// </summary>
    [System.Serializable]
    public class GalleryEntry
    {
        /// <summary>Unique ID, e.g. "fp_20260306_143022".</summary>
        public string id;

        /// <summary>OBJ filename, e.g. "FingerPaint_20260306_143022.obj".</summary>
        public string filename;

        /// <summary>ISO 8601 timestamp of when the work was saved.</summary>
        public string timestamp;

        /// <summary>Number of paint points (spheres) in the work.</summary>
        public int pointCount;

        /// <summary>Total vertex count in the exported mesh.</summary>
        public int vertexCount;
    }
}
