using System.IO;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Manages the gallery of saved finger-paint works.
    /// Handles manifest persistence (JSON) and OBJ mesh loading.
    /// </summary>
    public class GalleryManager : MonoBehaviour
    {
        // ─── Public state ──────────────────────────────────────────────

        /// <summary>The current gallery manifest with all saved works.</summary>
        public GalleryManifest Manifest { get; private set; }

        /// <summary>Number of saved works.</summary>
        public int WorkCount => Manifest?.works?.Count ?? 0;

        // ─── Private state ─────────────────────────────────────────────

        private string _saveDirectory;
        private string _manifestPath;

        // ─── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _saveDirectory = Path.Combine(Application.persistentDataPath, "SavedWorks");
            _manifestPath = Path.Combine(_saveDirectory, "gallery_manifest.json");
            LoadManifest();
        }

        // ─── Public API ────────────────────────────────────────────────

        /// <summary>Returns the save directory path.</summary>
        public string GetSaveDirectory()
        {
            return _saveDirectory;
        }

        /// <summary>
        /// Loads the manifest from disk. Creates a new one if none exists.
        /// </summary>
        public void LoadManifest()
        {
            if (File.Exists(_manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(_manifestPath);
                    Manifest = JsonUtility.FromJson<GalleryManifest>(json);
                    Debug.Log($"[GalleryManager] Loaded manifest with {Manifest.works.Count} works.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GalleryManager] Failed to load manifest: {ex.Message}");
                    Manifest = new GalleryManifest();
                }
            }
            else
            {
                Manifest = new GalleryManifest();
                Debug.Log("[GalleryManager] No manifest found — created new one.");
            }
        }

        /// <summary>
        /// Saves the current manifest to disk.
        /// </summary>
        public void SaveManifest()
        {
            try
            {
                if (!Directory.Exists(_saveDirectory))
                    Directory.CreateDirectory(_saveDirectory);

                string json = JsonUtility.ToJson(Manifest, prettyPrint: true);
                File.WriteAllText(_manifestPath, json);
                Debug.Log($"[GalleryManager] Manifest saved ({Manifest.works.Count} works).");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GalleryManager] Failed to save manifest: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a new entry to the manifest and persists it.
        /// </summary>
        public void AddEntry(GalleryEntry entry)
        {
            if (Manifest == null)
                Manifest = new GalleryManifest();

            Manifest.works.Add(entry);
            SaveManifest();
            Debug.Log($"[GalleryManager] Added entry: {entry.id} ({entry.filename})");
        }

        /// <summary>
        /// Removes an entry by ID and persists the manifest.
        /// </summary>
        public void DeleteEntry(string id)
        {
            if (Manifest == null) return;

            int idx = Manifest.works.FindIndex(w => w.id == id);
            if (idx < 0) return;

            // Delete the OBJ file
            string objPath = Path.Combine(_saveDirectory, Manifest.works[idx].filename);
            if (File.Exists(objPath))
            {
                try
                {
                    File.Delete(objPath);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GalleryManager] Failed to delete file: {ex.Message}");
                }
            }

            Manifest.works.RemoveAt(idx);
            SaveManifest();
        }

        /// <summary>
        /// Loads an OBJ file from the save directory and returns it as a Unity Mesh.
        /// Returns null if the file doesn't exist or parsing fails.
        /// </summary>
        public Mesh LoadObjMesh(string filename)
        {
            string path = Path.Combine(_saveDirectory, filename);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[GalleryManager] OBJ file not found: {path}");
                return null;
            }

            try
            {
                string objText = File.ReadAllText(path);
                Mesh mesh = ObjParser.Parse(objText);
                Debug.Log($"[GalleryManager] Loaded mesh: {filename} ({mesh.vertexCount} verts)");
                return mesh;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GalleryManager] Failed to parse OBJ: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the GalleryEntry at the given index, or null if out of range.
        /// </summary>
        public GalleryEntry GetEntry(int index)
        {
            if (Manifest == null || index < 0 || index >= Manifest.works.Count)
                return null;
            return Manifest.works[index];
        }
    }
}
