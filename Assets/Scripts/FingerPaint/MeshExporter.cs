using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace FingerPaint
{
    /// <summary>
    /// Combines all spawned paint spheres into a single mesh and exports as OBJ.
    /// Writes to /storage/emulated/0/Download/ on Quest (Android internal storage).
    /// Handles Android runtime permission requests.
    /// </summary>
    public class MeshExporter : MonoBehaviour
    {
        [SerializeField] private FingerPainter _painter;
        [SerializeField] private string _filePrefix = "FingerPaint";

        /// <summary>Max vertices per combined sub-mesh (Unity limit is 65535 for 16-bit index).</summary>
        private const int MaxVerticesPerBatch = 60000;

        private bool _permissionGranted;
        private bool _exportPending;

        private void Start()
        {
#if UNITY_EDITOR
            _permissionGranted = true;
#endif
        }

        /// <summary>
        /// Call this to trigger export. Requests permissions first if needed.
        /// </summary>
        public void Export()
        {
            if (_painter == null || _painter.TotalPointCount == 0)
            {
                Debug.LogWarning("[MeshExporter] No points to export.");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android 10+ (API 29+) with scoped storage, apps can write to
            // their own Download directory without WRITE_EXTERNAL_STORAGE.
            // But on Quest OS (Android-based), permission may still be needed.
            if (!_permissionGranted)
            {
                RequestAndroidPermission();
                _exportPending = true;
                return;
            }
#endif
            PerformExport();
        }

        private void RequestAndroidPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                _permissionGranted = true;
                if (_exportPending)
                    PerformExport();
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                _permissionGranted = true;
                if (_exportPending)
                    PerformExport();
            };
            callbacks.PermissionDenied += _ =>
            {
                Debug.LogError("[MeshExporter] Storage permission denied.");
                _exportPending = false;
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                Debug.LogError("[MeshExporter] Storage permission permanently denied.");
                _exportPending = false;
            };

            Permission.RequestUserPermission(Permission.ExternalStorageWrite, callbacks);
#endif
        }

        private void PerformExport()
        {
            _exportPending = false;

            var allPoints = _painter.GetAllPoints();
            if (allPoints.Count == 0)
                return;

            // Gather CombineInstances
            var combines = new List<CombineInstance>();
            foreach (var go in allPoints)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                combines.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = go.transform.localToWorldMatrix
                });
            }

            // Batch-combine respecting vertex limits
            var finalMeshes = CombineInBatches(combines);

            // Merge all batches into one final mesh (using 32-bit indices)
            var merged = new Mesh();
            merged.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var mergeCombines = new CombineInstance[finalMeshes.Count];
            for (int i = 0; i < finalMeshes.Count; i++)
            {
                mergeCombines[i] = new CombineInstance
                {
                    mesh = finalMeshes[i],
                    transform = Matrix4x4.identity
                };
            }
            merged.CombineMeshes(mergeCombines, true, false);
            merged.RecalculateNormals();
            merged.RecalculateBounds();

            // Write OBJ
            string filename = $"{_filePrefix}_{System.DateTime.Now:yyyyMMdd_HHmmss}.obj";
            string dir = GetExportDirectory();
            string path = Path.Combine(dir, filename);

            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string obj = MeshToObj(merged, _filePrefix);
                File.WriteAllText(path, obj);
                Debug.Log($"[MeshExporter] Exported {merged.vertexCount} vertices to: {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MeshExporter] Export failed: {ex.Message}");
            }

            // Cleanup temp meshes
            foreach (var m in finalMeshes)
                Destroy(m);
            Destroy(merged);
        }

        private List<Mesh> CombineInBatches(List<CombineInstance> all)
        {
            var result = new List<Mesh>();
            var batch = new List<CombineInstance>();
            int vertCount = 0;

            foreach (var ci in all)
            {
                int meshVerts = ci.mesh.vertexCount;
                if (vertCount + meshVerts > MaxVerticesPerBatch && batch.Count > 0)
                {
                    result.Add(FlushBatch(batch));
                    batch.Clear();
                    vertCount = 0;
                }
                batch.Add(ci);
                vertCount += meshVerts;
            }

            if (batch.Count > 0)
                result.Add(FlushBatch(batch));

            return result;
        }

        private Mesh FlushBatch(List<CombineInstance> batch)
        {
            var mesh = new Mesh();
            mesh.CombineMeshes(batch.ToArray(), true, true);
            return mesh;
        }

        private static string GetExportDirectory()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "/storage/emulated/0/Download";
#else
            // In-editor: write to project root for testing
            return Path.Combine(Application.dataPath, "..", "ExportedMeshes");
#endif
        }

        private static string MeshToObj(Mesh mesh, string objectName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Finger Paint Export");
            sb.AppendLine($"# Vertices: {mesh.vertexCount}");
            sb.AppendLine($"# Triangles: {mesh.triangles.Length / 3}");
            sb.AppendLine($"o {objectName}");

            var verts = mesh.vertices;
            var normals = mesh.normals;
            var tris = mesh.triangles;

            // Vertices (flip X for right-hand coordinate system)
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                sb.AppendLine($"v {-v.x:F6} {v.y:F6} {v.z:F6}");
            }

            // Normals
            if (normals != null && normals.Length == verts.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    var n = normals[i];
                    sb.AppendLine($"vn {-n.x:F6} {n.y:F6} {n.z:F6}");
                }
            }

            // Faces (OBJ is 1-indexed, flip winding order due to X flip)
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i] + 1;
                int b = tris[i + 1] + 1;
                int c = tris[i + 2] + 1;
                // Reverse winding: a c b
                sb.AppendLine($"f {a}//{a} {c}//{c} {b}//{b}");
            }

            return sb.ToString();
        }
    }
}
