using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Code Breaker editor tools.
//   Code Breaker > 1. Check Scene                 -> reports missing scripts / empty references (changes nothing)
//   Code Breaker > 2. Setup Escape Room           -> builds the room/question/checkpoint/session systems into the open level
//   Code Breaker > 3. Clean Level 2               -> removes Level 2's missing script and duplicate audio listener
//   Code Breaker > Run Question Pool Self-Test    -> checks the question rules automatically
//   Code Breaker > Play-Test > ...                -> helpers while in Play mode
// Setup never deletes your objects (only objects this tool itself created earlier). Ctrl+Z undoes it.
public static class CodeBreakerLevelSetup
{
    const float RoomSize = 30f;              // each room in Level 1 is 30 x 30 units
    const string Root = "Assets/CodeBreaker";
    const string Tag = "[Code Breaker Setup] ";
    const string QuestionBankPath = Root + "/Data/Level 1 Questions.asset";
    const string LampPrefabPath = "Assets/New Solution Studio/PBR Lamps Pack/Prefabs/Classic_fluorescent_lamp.prefab";
    const string CeilingMaterialPath = "Assets/Walls/Ceiling.mat";
    const string AudioFolder = Root + "/Audio";
    const string MixerPath = AudioFolder + "/CodeBreaker Audio Mixer.mixer";
    const string BloodTexturePath = Root + "/Textures/BloodSplatter.png";
    const string DoorAudioFolder = "Assets/Free Wood Door Pack/Audio";

    // Walk-in corridor behind every answer door (Level 1 doorway is about 6.4 units wide, doors are 10.5 tall).
    const float OpeningHalfWidth = 3.2f;
    const float PassageMargin = 1.2f;       // room for the door leaf to swing in
    const float PassageDepth = 14f;         // inner length, then the passage turns out through a side exit
    const float ExitWidth = 6.4f;           // side exit, same width as the doorways
    const float ExitHeight = 10.6f;
    const float DecideDepth = 3f;           // a few steps in: right or wrong is decided here
    const float PassageWall = 0.6f;
    const float PassageCeiling = 11.8f;     // lower ceiling inside the corridor
    const string DoorHorrorTexturePath = Root + "/Textures/Door_Horror_Albedo.png";
    const string DoorHorrorMaterialPath = Root + "/Materials/Door_Horror.mat";

    // ------------------------------------------------------------------ CHECK
    [MenuItem("Code Breaker/1. Check Scene (missing references)", false, 1)]
    public static void CheckScene()
    {
        var sb = new StringBuilder();
        int problems = 0;
        Scene scene = SceneManager.GetActiveScene();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0)
                {
                    problems++;
                    sb.AppendLine("MISSING SCRIPT on " + PathOf(t));
                }

                foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    MonoScript ms = MonoScript.FromMonoBehaviour(mb);
                    string path = ms != null ? AssetDatabase.GetAssetPath(ms) : "";
                    if (!IsProjectScript(path)) continue;

                    var so = new SerializedObject(mb);
                    SerializedProperty p = so.GetIterator();
                    bool enter = true;
                    while (p.NextVisible(enter))
                    {
                        enter = p.propertyType == SerializedPropertyType.Generic;
                        if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (p.name == "m_Script") continue;
                        if (p.objectReferenceValue != null) continue;
                        if (IsOptional(mb, p.propertyPath)) continue;
                        problems++;
                        sb.AppendLine("EMPTY  " + mb.GetType().Name + "." + p.propertyPath + "  on " + PathOf(t));
                    }
                }
            }
        }

        if (problems == 0)
            Debug.Log(Tag + "Scene '" + scene.name + "': no missing scripts or empty references found.");
        else
            Debug.LogWarning(Tag + "Scene '" + scene.name + "': " + problems + " issue(s)\n" + sb);
    }

    static bool IsProjectScript(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return false;
        string[] packs = { "Assets/TextMesh Pro", "Assets/SyntyStudios", "Assets/Free Wood Door Pack", "Assets/Phoenix3D",
                           "Assets/ALP_Assets", "Assets/Blue Olive Studio", "Assets/Vamporium Language", "Assets/UIImporterFree",
                           "Assets/TutorialInfo", "Assets/CaptainCatSparrow", "Assets/New Solution Studio" };
        return !packs.Any(path.StartsWith);
    }

    // Fields that are allowed to be empty.
    static bool IsOptional(MonoBehaviour mb, string field)
    {
        if (mb is DoorInteraction d)
        {
            if (field == "fireHazard") return true;                          // room system handles fire now
            if (field == "tryAgainButton" && d.isCorrectDoor) return true;
        }
        if (mb is PlayerHealth && (field == "damageOverlay" || field == "bloodOverlay")) return true;
        if (mb is AudioManager && (field == "mixer" || field == "musicGroup" || field == "sfxGroup" || field == "doorCreak")) return true;
        if (mb is GameFlowManager && (field == "fadeOverlay" || field == "settingsMenu")) return true;
        return false;
    }

    // ------------------------------------------------------------------ SETUP
    [MenuItem("Code Breaker/2. Setup Escape Room (current scene)", false, 2)]
    public static void SetupEscapeRoom()
    {
        Scene scene = SceneManager.GetActiveScene();
        var log = new StringBuilder();
        Undo.SetCurrentGroupName("Code Breaker Setup");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject player = FindRoot(scene, "Player");
        DoorInteraction[] doors = All<DoorInteraction>(scene);
        LecternQuestion[] lecterns = All<LecternQuestion>(scene);
        if (player == null || doors.Length == 0)
        {
            EditorUtility.DisplayDialog("Code Breaker", "This scene needs a 'Player' and at least one DoorInteraction.", "OK");
            return;
        }

        // 0. Remove components whose script no longer exists ------------------------------
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n == 0) continue;
                Undo.RegisterCompleteObjectUndo(t.gameObject, "Remove missing script");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                log.AppendLine("Removed " + n + " missing script component(s) from " + PathOf(t));
            }

        // 1. Organise hierarchy ------------------------------------------------------------
        Transform envGroup = Group(scene, "Environment");
        Transform doorGroup = Group(scene, "Doors");
        Transform lecternGroup = Group(scene, "Lecterns");
        Transform hazardGroup = Group(scene, "Hazards");
        Transform lightGroup = Group(scene, "Lighting");
        Transform uiGroup = Group(scene, "UI");
        Transform sysGroup = Group(scene, "Systems");
        Transform checkpointGroup = Group(scene, "Checkpoints");
        Transform session = Group(scene, "Game Session");

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            string n = go.name;
            if (n == "Floor" || n == "Big Wall" || n == "Small Wall" || n == "Roof") Move(go, envGroup, log);
            else if (n.StartsWith("Door ")) Move(go, doorGroup, log);
            else if (n.StartsWith("Lecterm") || n.StartsWith("Lectern")) Move(go, lecternGroup, log);
            else if (n == "Directional Light" || n == "Global Volume") Move(go, lightGroup, log);
            else if (n == "EventSystem") Move(go, sysGroup, log);
            else if (go.GetComponent<Canvas>() != null) Move(go, uiGroup, log);
        }
        foreach (Transform t in lecternGroup)
            if (t.name.StartsWith("Lecterm"))
            {
                Undo.RecordObject(t.gameObject, "Rename");
                t.name = t.name.Replace("Lecterm", "Lectern");
            }

        // 2. Ceiling (height + reference ceiling material) ----------------------------------
        Bounds walls = BoundsOf(envGroup, "Big Wall", "Small Wall");
        Transform roof = envGroup.Find("Roof");
        if (roof != null && walls.size != Vector3.zero)
        {
            Renderer rr = roof.GetComponent<Renderer>();
            float thickness = rr != null ? rr.bounds.size.y : 1f;
            float targetY = walls.max.y + thickness * 0.5f;
            if (Mathf.Abs(roof.position.y - targetY) > 0.05f)
            {
                Undo.RecordObject(roof, "Move Roof");
                roof.position = new Vector3(roof.position.x, targetY, roof.position.z);
                log.AppendLine("Moved Roof to the top of the walls (y=" + targetY.ToString("0.00") + ")");
            }
            Material ceilingMat = AssetDatabase.LoadAssetAtPath<Material>(CeilingMaterialPath);
            if (rr != null && ceilingMat != null && rr.sharedMaterial != ceilingMat)
            {
                Undo.RecordObject(rr, "Ceiling material");
                rr.sharedMaterial = ceilingMat;
                log.AppendLine("Roof now uses the reference Ceiling material");
            }
        }

        // 3. Work out the rooms from the floor and doors ------------------------------------
        Bounds floor = BoundsOf(envGroup, "Floor");
        Vector2 origin = new Vector2(floor.min.x, floor.min.z);
        float floorY = floor.max.y;
        float ceilingY = walls.size != Vector3.zero ? walls.max.y : floorY + 14f;

        Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt((p.x - origin.x) / RoomSize), Mathf.FloorToInt((p.z - origin.y) / RoomSize));
        Vector3 CellCenter(Vector2Int c) => new Vector3(origin.x + (c.x + 0.5f) * RoomSize, floorY, origin.y + (c.y + 0.5f) * RoomSize);

        var doorSides = new Dictionary<DoorInteraction, (Vector2Int a, Vector2Int b)>();
        foreach (DoorInteraction d in doors)
        {
            Vector3 p = DoorRoot(d).position;
            Vector3 n = Flat(DoorRoot(d).right);
            doorSides[d] = (CellOf(p + n * RoomSize * 0.5f), CellOf(p - n * RoomSize * 0.5f));
        }

        // Follow the correct doors from the player's start room -> ordered list of rooms.
        var roomCells = new List<Vector2Int> { CellOf(player.transform.position) };
        var pathDoors = new List<DoorInteraction>();
        var visited = new HashSet<Vector2Int>(roomCells);
        Vector2Int exitCell = roomCells[0];
        while (true)
        {
            Vector2Int c = roomCells[roomCells.Count - 1];
            DoorInteraction next = doors.FirstOrDefault(d => d.isCorrectDoor && !pathDoors.Contains(d) &&
                (doorSides[d].a == c || doorSides[d].b == c) && !(visited.Contains(doorSides[d].a) && visited.Contains(doorSides[d].b)));
            if (next == null) break;
            Vector2Int target = doorSides[next].a == c ? doorSides[next].b : doorSides[next].a;
            pathDoors.Add(next);
            visited.Add(target);
            exitCell = target;
            if (doors.Any(d => d.isCorrectDoor && !pathDoors.Contains(d) && (doorSides[d].a == target || doorSides[d].b == target)))
                roomCells.Add(target);
            else break;   // target has no further correct door: it is the exit room
        }

        Vector2Int TargetOf(DoorInteraction d, Vector2Int from) => doorSides[d].a == from ? doorSides[d].b : doorSides[d].a;
        Vector3 IntoCell(DoorInteraction d, Vector2Int cell)
        {
            Vector3 n = Flat(DoorRoot(d).right);
            return Vector3.Dot(CellCenter(cell) - DoorRoot(d).position, n) >= 0 ? n : -n;
        }

        // 4. Fire rooms behind the wrong doors (reuses the ones made earlier) ----------------
        Material fireMat = GetOrCreateFireMaterial(log);
        var roomTraps = new List<(DoorInteraction door, FireHazard fire, Vector2Int cell)>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            Vector2Int c = roomCells[i];
            DoorInteraction trap = doors.FirstOrDefault(d => !d.isCorrectDoor && (doorSides[d].a == c || doorSides[d].b == c));
            if (trap == null) { roomTraps.Add((null, null, c)); continue; }
            Vector2Int trapCell = TargetOf(trap, c);
            Vector3 centre = CellCenter(trapCell) + Vector3.up * 3f;
            FireHazard fire = All<FireHazard>(scene).FirstOrDefault(f => f.GetComponent<BoxCollider>() != null && f.GetComponent<BoxCollider>().bounds.Contains(centre));
            if (fire == null) fire = CreateFireRoom(hazardGroup, trap, trapCell, CellCenter(trapCell), floorY, ceilingY, fireMat, log);
            Undo.RecordObject(fire, "Fire visual only");
            fire.damagePlayer = false;
            roomTraps.Add((trap, fire, trapCell));
        }

        // 5. Lighting like the reference: ceiling lamp + warm point light in every room -------
        Transform oldLights = lightGroup.Find("Room Lights");   // plain lights this tool added earlier
        if (oldLights != null)
        {
            Undo.DestroyObjectImmediate(oldLights.gameObject);
            log.AppendLine("Replaced the earlier plain Room Lights with reference-style ceiling lamps");
        }
        if (lightGroup.Find("Ceiling Lamps") == null)
        {
            GameObject lampsRoot = new GameObject("Ceiling Lamps");
            Undo.RegisterCreatedObjectUndo(lampsRoot, "Lamps");
            lampsRoot.transform.SetParent(lightGroup, false);
            GameObject lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
            int count = 0;
            for (int i = 0; i < roomCells.Count; i++) { CreateLamp(lampsRoot.transform, "Room " + (i + 1), CellCenter(roomCells[i]), ceilingY, lampPrefab, 40f, 64.7f); count++; }
            CreateLamp(lampsRoot.transform, "Exit Room", CellCenter(exitCell), ceilingY, lampPrefab, 40f, 64.7f); count++;
            foreach (var t in roomTraps.Where(x => x.door != null)) { CreateLamp(lampsRoot.transform, "Fire Room (" + DoorRoot(t.door).name + ")", CellCenter(t.cell), ceilingY, lampPrefab, 22f, 38f); count++; }
            log.AppendLine("Added " + count + " ceiling lamps (fluorescent lamp model + warm point light, values from the reference)");
        }
        Light sun = lightGroup.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null && sun.color != Color.black)
        {
            Undo.RecordObject(sun, "Sun");
            sun.color = Color.black;   // the reference turns the sun off; rooms are lit by the lamps
            log.AppendLine("Directional Light colour set to black like the reference");
        }

        // 6. Question bank (single source of truth) -------------------------------------------
        QuestionBank bank = GetOrCreateQuestionBank(log);

        // 7. Lectern UI restyled like the reference -------------------------------------------
        TMP_Text noteText = null;
        GameObject questionCanvas = lecterns.Select(l => l.questionCanvas).FirstOrDefault(c => c != null);
        if (questionCanvas != null)
        {
            Transform bg = questionCanvas.transform.Find("BG PANEL");
            if (bg != null && bg.GetComponent<Image>() is Image bgImg)
            {
                Undo.RecordObject(bgImg, "Lectern BG");
                bgImg.color = new Color(0.07924521f, 0.058462027f, 0.058462027f, 0.392f);   // reference value
            }
            noteText = questionCanvas.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "NoteText");
            if (noteText != null)
            {
                Undo.RecordObject(noteText, "Note text");
                Undo.RecordObject(noteText.rectTransform, "Note text rect");
                RectTransform rt = noteText.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(24, 28); rt.offsetMax = new Vector2(-24, -34);
                noteText.margin = Vector4.zero;   // old negative margins were made for the previous tiny text box
                noteText.enableAutoSizing = true;
                noteText.fontSizeMin = 8; noteText.fontSizeMax = 15;
                noteText.alignment = TextAlignmentOptions.Top;
                noteText.fontStyle = FontStyles.Normal;
                noteText.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                noteText.textWrappingMode = TextWrappingModes.Normal;
                noteText.text = "";
            }
        }
        foreach (LecternQuestion l in lecterns)
        {
            Undo.RecordObject(l, "Lectern text");
            l.questionText = noteText;
        }

        // 8. Answer signs above both doors of every room ----------------------------------------
        Material panelMat = GetOrCreateUnlitMaterial("SignPanel", new Color(0.11f, 0.09f, 0.075f), log);
        Material frameMat = GetOrCreateUnlitMaterial("SignFrame", new Color(0.55f, 0.45f, 0.30f), log);
        var signs = new Dictionary<DoorInteraction, AnswerSign>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            Vector2Int c = roomCells[i];
            foreach (DoorInteraction d in new[] { i < pathDoors.Count ? pathDoors[i] : null, roomTraps[i].door })
            {
                if (d == null) continue;
                signs[d] = GetOrCreateSign(d, IntoCell(d, c), panelMat, frameMat, log);
            }
        }

        // 8b. Destinations decided by the current answer ---------------------------------------------
        // The corridors/vestibules from earlier versions (made by this tool) are removed: every door
        // opens straight into a room again. Each room's fire room is dressed as a copy of the real next
        // room (lectern + two doors, decoration only), laid out identically relative to its door, and a
        // trigger just past each door lets the RoomManager send the player to the right one.
        foreach (string old in new[] { "Door Vestibules", "Door Passages" })
        {
            Transform o = envGroup.Find(old);
            if (o != null) { Undo.DestroyObjectImmediate(o.gameObject); log.AppendLine("Removed the old '" + old + "' (replaced by duplicate rooms)"); }
        }

        Transform thrGroup = sysGroup.Find("Door Thresholds");
        if (thrGroup != null) Undo.DestroyObjectImmediate(thrGroup.gameObject);
        GameObject thrRoot = new GameObject("Door Thresholds");
        Undo.RegisterCreatedObjectUndo(thrRoot, "Thresholds");
        thrRoot.transform.SetParent(sysGroup, false);

        Transform decor = envGroup.Find("Trap Room Decor");
        if (decor != null) Undo.DestroyObjectImmediate(decor.gameObject);
        GameObject decorRoot = new GameObject("Trap Room Decor");
        Undo.RegisterCreatedObjectUndo(decorRoot, "Trap decor");
        decorRoot.transform.SetParent(envGroup, false);
        Physics.SyncTransforms();

        var thresholds = new Dictionary<DoorInteraction, DoorThreshold>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            DoorInteraction realDoor = i < pathDoors.Count ? pathDoors[i] : null;
            DoorInteraction trapDoor = roomTraps[i].door;
            if (realDoor == null || trapDoor == null) continue;

            foreach (DoorInteraction d in new[] { realDoor, trapDoor })
                thresholds[d] = CreateThreshold(thrRoot.transform, d, -IntoCell(d, roomCells[i]), d == realDoor, floorY);

            // Copy what is in the real next room (seen through its door) into the trap room,
            // in the same place relative to the trap door.
            Vector2Int realCell = TargetOf(realDoor, roomCells[i]);
            Transform from = thresholds[realDoor].transform, to = thresholds[trapDoor].transform;
            GameObject roomDecor = new GameObject("Trap Room (" + DoorRoot(trapDoor).name + ")");
            roomDecor.transform.SetParent(decorRoot.transform, false);
            int copied = 0;

            foreach (LecternQuestion lq in lecterns.Where(l => l != null && CellOf(l.transform.position) == realCell))
            {
                Transform src = TopUnder(lecternGroup, lq.transform);
                CopyAsDecor(src, from, to, roomDecor.transform, Vector3.zero);
                copied++;
            }
            foreach (DoorInteraction d in doors.Where(x => x != null && x != realDoor && doorSides.ContainsKey(x) &&
                                                         (doorSides[x].a == realCell || doorSides[x].b == realCell)))
            {
                // a door on a wall of the real room; in the trap room it sits on a solid wall,
                // so pull it forward just enough that the leaf shows (it is inside the wall otherwise)
                Vector3 intoReal = IntoCell(d, realCell);
                Vector3 intoTrap = to.rotation * (Quaternion.Inverse(from.rotation) * intoReal);
                Vector3 plane = to.TransformPoint(from.InverseTransformPoint(DoorOpeningCentre(d)));
                bool hitWall = Physics.Raycast(new Vector3(plane.x, floorY + 5f, plane.z) + intoTrap * 3f, -intoTrap,
                                               out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore);
                GameObject copy = CopyAsDecor(DoorRoot(d), from, to, roomDecor.transform, Vector3.zero);
                if (!hitWall) log.AppendLine("  copy of " + DoorRoot(d).name + ": no wall found behind it");
                if (hitWall)
                {
                    // bring the door leaf just in front of the wall surface
                    // leaf face = centre of the door leaf + half its thickness (the push bars stick out
                    // further, so the renderer bounds can't be used for the face itself)
                    float surface = Vector3.Dot(hit.point, intoTrap);
                    bool found = false; Bounds leaf = default;
                    foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
                        if (r.sharedMaterials.Any(m => m != null && m.name.StartsWith("Door_Horror")))
                        {
                            if (!found) { leaf = r.bounds; found = true; } else leaf.Encapsulate(r.bounds);
                        }
                    if (found)
                    {
                        // the push bar is on one side only, so the flat panel isn't at the centre of the
                        // leaf's bounds: put the BACK of the whole leaf just in front of the wall instead
                        Vector3 an = new Vector3(Mathf.Abs(intoTrap.x), Mathf.Abs(intoTrap.y), Mathf.Abs(intoTrap.z));
                        float leafBack = Vector3.Dot(leaf.center, intoTrap) - Vector3.Dot(leaf.extents, an);
                        float leafFace = leafBack;
                        float shift = surface + 0.03f - leafBack;
                        if (shift > 0f) copy.transform.position += intoTrap * shift;
                        log.AppendLine("  copy of " + DoorRoot(d).name + ": wall hit '" + hit.collider.name + "' surface=" + surface.ToString("0.00") +
                                       " leafFace=" + leafFace.ToString("0.00") + " leafSize=" + leaf.size.ToString("F2") + " shift=" + shift.ToString("0.00"));
                    }
                    else log.AppendLine("  copy of " + DoorRoot(d).name + ": no door leaf renderer found (materials: " +
                                        string.Join(",", copy.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct()) + ")");
                }
                copied++;
            }
            // The ceiling lamps: same fixture, same orientation relative to the door (the lamp of the trap room
            // itself points the other way, so it is switched off and replaced by a copy of the real room's lamp).
            Vector2Int trapCell = TargetOf(trapDoor, roomCells[i]);
            Transform lampGroup = lightGroup.Find("Ceiling Lamps");
            if (lampGroup != null)
                foreach (Transform lamp in lampGroup.Cast<Transform>().ToArray())
                {
                    Vector2Int lc = CellOf(lamp.position);
                    if (lc == realCell) { CopyAsDecor(lamp, from, to, roomDecor.transform, Vector3.zero); copied++; }
                    else if (lc == trapCell && lamp.gameObject.activeSelf)
                    {
                        Undo.RecordObject(lamp.gameObject, "Trap lamp");
                        lamp.gameObject.SetActive(false);
                        log.AppendLine("Switched off " + lamp.name + " (replaced by a copy of the real room's lamp)");
                    }
                }

            log.AppendLine("Room " + (i + 1) + ": " + DoorRoot(realDoor).name + " opens into the real next room, " + DoorRoot(trapDoor).name +
                           " into the trap room (" + copied + " decoration copies: lectern + doors, not interactive). The answer decides which one you reach.");
        }

        // Where the player stands in each burning room after a wrong answer (facing the flames).
        var fireSpots = new List<Transform>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            var t = roomTraps[i];
            if (t.door == null || t.fire == null) { fireSpots.Add(null); continue; }
            string name = "Room " + (i + 1) + " Fire Room Spot";
            Transform spot = checkpointGroup.Find(name);
            if (spot == null)
            {
                GameObject g = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(g, "Fire spot");
                g.transform.SetParent(checkpointGroup, false);
                spot = g.transform;
            }
            Vector3 centre = CellCenter(t.cell);
            Vector3 toDoor = Flat(DoorOpeningCentre(t.door) - centre);
            Vector3 p = centre + toDoor * 5f;
            p.y = player.transform.position.y;
            Undo.RecordObject(spot, "Fire spot");
            spot.SetPositionAndRotation(p, Quaternion.LookRotation(-toDoor));
            fireSpots.Add(spot);
        }

        // 9. Checkpoints at the start of every room ---------------------------------------------
        var checkpoints = new List<Transform>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            string name = "Room " + (i + 1) + " Checkpoint";
            Transform cp = checkpointGroup.Find(name);
            if (cp == null)
            {
                GameObject g = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(g, "Checkpoint");
                g.transform.SetParent(checkpointGroup, false);
                cp = g.transform;
            }
            Undo.RecordObject(cp, "Checkpoint");
            if (i == 0)
            {
                cp.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            }
            else
            {
                // just inside the door you come in through, facing into the room
                DoorInteraction entry = pathDoors[i - 1];
                Vector3 inward = IntoCell(entry, roomCells[i]);
                Vector3 p = DoorOpeningCentre(entry) + inward * 5f;
                p.y = player.transform.position.y;
                cp.SetPositionAndRotation(p, Quaternion.LookRotation(inward));
            }
            checkpoints.Add(cp);
        }
        log.AppendLine("Checkpoints set for " + checkpoints.Count + " rooms");

        // 10. Game Session (persistent: health, timer, HUD, result screens) -----------------------
        DoorInteraction firstDoor = doors.FirstOrDefault(x => x.resultCanvas != null) ?? doors[0];
        GameFlowManager gm = session.GetComponent<GameFlowManager>();
        if (gm == null) gm = Undo.AddComponent<GameFlowManager>(session.gameObject);
        foreach (GameFlowManager other in All<GameFlowManager>(scene).Where(x => x != gm).ToArray())
        {
            log.AppendLine("Removed the old Game Flow Manager object (its job moved to Game Session)");
            Undo.DestroyObjectImmediate(other.gameObject.name == "Game Flow Manager" ? (Object)other.gameObject : other);
        }

        PlayerHealth health = MoveComponentTo<PlayerHealth>(scene, session.gameObject, log);
        LevelTimer timer = MoveComponentTo<LevelTimer>(scene, session.gameObject, log);
        Transform oldTimerObj = sysGroup.Find("Level Timer");
        if (oldTimerObj != null && oldTimerObj.GetComponents<Component>().Length == 1)
            Undo.DestroyObjectImmediate(oldTimerObj.gameObject);

        Transform hud = uiGroup.Find("HUD Canvas") ?? session.Find("HUD Canvas");
        if (hud == null) hud = CreateHud(session, health, timer, log);
        Move(hud.gameObject, session, log);
        if (firstDoor.resultCanvas != null) Move(firstDoor.resultCanvas, session, log);

        TMP_Text location = hud.Find("Location")?.GetComponent<TMP_Text>();
        if (location == null)
        {
            location = UIText("Location", hud, "LEVEL 1  ·  ROOM 1 / 3", 20, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(location.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -136), new Vector2(700, 30));
            location.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
            Undo.RegisterCreatedObjectUndo(location.gameObject, "Location");
        }
        if (health.wrongAnswerDamage == null || health.wrongAnswerDamage.Length == 0 || health.wrongAnswerDamage.Sum() != 100)
        {
            Undo.RecordObject(health, "Damage table");
            health.wrongAnswerDamage = new[] { 35, 35, 30 };
        }
        Undo.RecordObject(health, "Health");
        health.maxHealth = 100f;

        Undo.RecordObject(gm, "Game flow");
        gm.playerHealth = health;
        gm.levelTimer = timer;
        gm.locationText = location;
        gm.resultCanvas = firstDoor.resultCanvas;
        gm.resultTitle = firstDoor.resultTitle;
        gm.resultDescription = firstDoor.resultDescription;
        gm.tryAgainButton = doors.Select(x => x.tryAgainButton).FirstOrDefault(x => x != null);
        gm.firstLevelScene = "Level 1";
        gm.finalLevelScene = "Level 3";
        gm.levelTransitionDelay = 6f;

        // Time limit per level (Level 1 = 3:00, Level 2 = 6:00, Level 3 = to be decided -> default)
        Undo.RecordObject(timer, "Timer limits");
        if (timer.levelTimeLimits == null) timer.levelTimeLimits = new List<LevelTimeLimit>();
        SetLimit(timer, "Level 1", 180f, log);
        SetLimit(timer, "Level 2", 360f, log);
        if (!timer.levelTimeLimits.Any(l => l.sceneName == "Level 3"))
            timer.levelTimeLimits.Add(new LevelTimeLimit { sceneName = "Level 3", seconds = 0f });
        timer.timeLimit = 360f;

        StyleResultCanvas(gm, log);

        // Blood splatter + fade + audio settings on the HUD, audio on the Game Session -----------
        SetupOverlays(hud, health, gm, log);
        SetupAudio(session.gameObject, gm, hud, log);

        // Player: collision capsule as wide as the body, camera near clip small enough to never see into walls
        FixPlayerCollision(player, "Level 1 Player", log);
        CreateBurnEffect(player, fireMat, log);
        FixPlayerPrefab(log);

        // Door leaves: collider = the door mesh itself (no oversized invisible slab, no thinner-than-mesh gaps)
        FitDoorLeafColliders(doors, log);

        // Horror look for the doors: same model, pivot and colliders, new material only
        ApplyHorrorDoorMaterial(doors, log);

        if (firstDoor.resultCanvas != null && firstDoor.resultCanvas.GetComponent<Canvas>() is Canvas rc && rc.sortingOrder < 10)
        {
            Undo.RecordObject(rc, "Result order");
            rc.sortingOrder = 10;
        }

        // 11. Room Manager ------------------------------------------------------------------------
        RoomManager rm = All<RoomManager>(scene).FirstOrDefault();
        if (rm == null)
        {
            GameObject g = new GameObject("Room Manager");
            Undo.RegisterCreatedObjectUndo(g, "Room Manager");
            g.transform.SetParent(sysGroup, false);
            rm = g.AddComponent<RoomManager>();
        }
        Undo.RecordObject(rm, "Rooms");
        rm.questionBank = bank;
        rm.levelDisplayName = scene.name;
        rm.fadeTime = 0.45f;
        rm.fireDuration = 2.4f;
        rm.burnTime = 3f;
        rm.burnSpeedFactor = 0.6f;
        rm.rooms = new List<EscapeRoom>();
        for (int i = 0; i < roomCells.Count; i++)
        {
            Vector2Int c = roomCells[i];
            // door1/door2 are just the room's two doors; which one is correct is decided at random at runtime.
            DoorInteraction a = i < pathDoors.Count ? pathDoors[i] : null;
            DoorInteraction b = roomTraps[i].door;
            rm.rooms.Add(new EscapeRoom
            {
                roomName = "Room " + (i + 1),
                checkpoint = checkpoints[i],
                lectern = lecterns.FirstOrDefault(l => CellOf(l.transform.position) == c),
                door1 = a,
                sign1 = a != null && signs.ContainsKey(a) ? signs[a] : null,
                door2 = b,
                sign2 = b != null && signs.ContainsKey(b) ? signs[b] : null,
                threshold1 = a != null && thresholds.ContainsKey(a) ? thresholds[a] : null,
                threshold2 = b != null && thresholds.ContainsKey(b) ? thresholds[b] : null,
                fireRoom = roomTraps[i].fire,
                fireRoomSpot = fireSpots[i]
            });
            log.AppendLine("Room " + (i + 1) + ": lectern=" + (rm.rooms[i].lectern ? rm.rooms[i].lectern.transform.parent.name : "NONE") +
                           ", doors=" + (a ? DoorRoot(a).name : "NONE") + " / " + (b ? DoorRoot(b).name : "NONE") +
                           " (correct door picked at random per question)");
        }

        // 12. Doors: the room system decides results now -------------------------------------------
        foreach (DoorInteraction d in doors)
        {
            Undo.RecordObject(d, "Door");
            d.fireHazard = null;
            d.isFinalDoor = false;
            d.lockAfterChoice = false;   // closing the door inside a corridor must not trap the player
        }

        // 13. Build settings -------------------------------------------------------------------------
        EnsureLevelsInBuild(log);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(Tag + "Setup finished for '" + scene.name + "' and the scene was saved.\n" + log);
        CheckScene();
    }

    // ------------------------------------------------------------------ LEVEL 2 CLEAN-UP
    [MenuItem("Code Breaker/3. Update Level 2 (cleanup + player and door collision fixes)", false, 3)]
    public static void CleanLevel2()
    {
        const string path = "Assets/Scenes/Level 2.unity";
        if (!File.Exists(path)) { Debug.LogWarning(Tag + "Level 2 not found."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var log = new StringBuilder();
        foreach (GameObject root in s.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n > 0) { GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject); log.AppendLine("Removed missing script from " + PathOf(t)); }
            }

        var listeners = s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AudioListener>(true)).Where(a => a.enabled).ToList();
        if (listeners.Count > 1)
            foreach (AudioListener a in listeners.Where(a => a.GetComponentInParent<CharacterController>() == null))
            {
                a.enabled = false;
                log.AppendLine("Disabled extra AudioListener on " + PathOf(a.transform) + " (the Player camera keeps its listener)");
            }

        foreach (CharacterController cc in s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterController>(true)))
            FixPlayerCollision(cc.gameObject, "Level 2 Player", log);
        FitDoorLeafColliders(s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DoorInteraction>(true)), log);

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s);
        EditorSceneManager.CloseScene(s, true);
        Debug.Log(Tag + "Level 2 cleaned.\n" + (log.Length > 0 ? log.ToString() : "Nothing to fix."));
    }

    // ------------------------------------------------------------------ QUESTION POOL SELF-TEST
    [MenuItem("Code Breaker/Run Question Pool Self-Test", false, 20)]
    public static void QuestionPoolSelfTest()
    {
        var errors = new List<string>();
        var firstQuestions = new HashSet<int>();
        var rng = new System.Random(12345);
        const int rooms = 3, questions = 15, games = 3000;

        for (int g = 0; g < games; g++)
        {
            var pool = new QuestionPool(questions, g);
            for (int r = 0; r < rooms; r++)
                if (pool.AssignNew(r) < 0) errors.Add("game " + g + ": room " + r + " got no question at start");
            firstQuestions.Add(pool.CurrentFor(0));

            int current = 0;
            var frozen = new Dictionary<int, int>();   // completed room -> its question (must never change)
            for (int step = 0; step < 80 && current < rooms; step++)
            {
                if (rng.NextDouble() < 0.7)
                {
                    int q = pool.AssignNew(current);
                    if (q < 0) errors.Add("game " + g + ": room " + current + " ran out of questions");
                }
                else
                {
                    frozen[current] = pool.CurrentFor(current);
                    current++;
                }

                for (int a = 0; a < rooms; a++)
                {
                    if (!pool.OwnedBy(a).Contains(pool.CurrentFor(a)))
                        errors.Add("game " + g + ": room " + a + " shows a question it does not own");
                    for (int b = a + 1; b < rooms; b++)
                        if (pool.OwnedBy(a).Intersect(pool.OwnedBy(b)).Any())
                            errors.Add("game " + g + ": rooms " + a + " and " + b + " share a question");
                }
                foreach (var kv in frozen)
                    if (pool.CurrentFor(kv.Key) != kv.Value)
                        errors.Add("game " + g + ": completed room " + kv.Key + " changed question");
                if (errors.Count > 20) break;
            }
            if (errors.Count > 20) break;
        }

        // Exhaustion: one room keeps answering wrong long after the 15 questions are used up.
        var p2 = new QuestionPool(questions, 7);
        for (int r = 0; r < rooms; r++) p2.AssignNew(r);
        int prev = p2.CurrentFor(2);
        for (int i = 0; i < 100; i++)
        {
            int q = p2.AssignNew(2);
            if (q < 0) { errors.Add("exhaustion: AssignNew returned -1"); break; }
            if (p2.UnusedCount == 0 && p2.OwnedBy(2).Count > 1 && q == prev) errors.Add("exhaustion: same question twice in a row");
            if (p2.OwnerOf(q) != 2) errors.Add("exhaustion: room 2 got a question owned by room " + p2.OwnerOf(q));
            prev = q;
        }
        if (p2.OwnedBy(0).Count != 1 || p2.OwnedBy(1).Count != 1) errors.Add("exhaustion: rooms 0/1 lost or gained questions");

        string summary = games + " random games + 100-answer exhaustion test. Distinct first questions seen: " + firstQuestions.Count + "/15.";
        if (firstQuestions.Count < 10) errors.Add("questions do not look random");

        if (errors.Count == 0)
            Debug.Log(Tag + "Question Pool Self-Test PASSED. " + summary);
        else
            Debug.LogError(Tag + "Question Pool Self-Test FAILED (" + errors.Count + ")\n" + string.Join("\n", errors.Take(20)) + "\n" + summary);
    }

    // ------------------------------------------------------------------ PLAY-MODE TEST HELPERS
    [MenuItem("Code Breaker/Play-Test/Go to Door 1", false, 100)] static void T1() => GoToDoor("Door 1");
    [MenuItem("Code Breaker/Play-Test/Go to Door 2", false, 101)] static void T2() => GoToDoor("Door 2");
    [MenuItem("Code Breaker/Play-Test/Go to Door 3", false, 102)] static void T3() => GoToDoor("Door 3");
    [MenuItem("Code Breaker/Play-Test/Go to Door 4", false, 103)] static void T4() => GoToDoor("Door 4");
    [MenuItem("Code Breaker/Play-Test/Go to Door 5", false, 104)] static void T5() => GoToDoor("Door 5");
    [MenuItem("Code Breaker/Play-Test/Go to Door 6", false, 105)] static void T6() => GoToDoor("Door 6");
    [MenuItem("Code Breaker/Play-Test/Step Through Nearest Door", false, 120)] static void TStep() => StepThrough();
    [MenuItem("Code Breaker/Play-Test/Go to Current Room Lectern", false, 121)] static void TLect() => GoToLectern();
    [MenuItem("Code Breaker/Play-Test/Log Room and Question State", false, 122)]
    static void TLog() { if (RoomManager.Instance != null) Debug.Log(Tag + RoomManager.Instance.DebugState()); }
    [MenuItem("Code Breaker/Play-Test/Log Audio State", false, 123)]
    static void TAudio() { if (AudioManager.Instance != null) Debug.Log(Tag + "AUDIO\n" + AudioManager.Instance.DebugState()); }
    [MenuItem("Code Breaker/Play-Test/Log Audio State", true)] static bool VA() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Set Timer to 33 Seconds", false, 139)]
    static void TTimer33() { LevelTimer t = Object.FindAnyObjectByType<LevelTimer>(); if (t != null) t.SetRemainingTime(33f); }
    [MenuItem("Code Breaker/Play-Test/Set Timer to 33 Seconds", true)] static bool VT33() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Set Timer to 10 Seconds", false, 140)]
    static void TTimer() { LevelTimer t = Object.FindAnyObjectByType<LevelTimer>(); if (t != null) t.SetRemainingTime(10f); }
    [MenuItem("Code Breaker/Play-Test/Treat Current Level as Final (test)", false, 141)]
    static void TFinal() { if (GameFlowManager.Instance != null) GameFlowManager.Instance.finalLevelScene = SceneManager.GetActiveScene().name; }

    [MenuItem("Code Breaker/Play-Test/Go to Door 1", true)] static bool V1() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Door 2", true)] static bool V2() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Door 3", true)] static bool V3() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Door 4", true)] static bool V4() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Door 5", true)] static bool V5() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Door 6", true)] static bool V6() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Step Through Nearest Door", true)] static bool VS() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Go to Current Room Lectern", true)] static bool VL() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Log Room and Question State", true)] static bool VG() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Set Timer to 10 Seconds", true)] static bool VT() => Application.isPlaying;
    [MenuItem("Code Breaker/Play-Test/Treat Current Level as Final (test)", true)] static bool VF() => Application.isPlaying;

    // Puts the player in front of a door (on the side it is reached from), facing it.
    static void GoToDoor(string doorName)
    {
        GameObject player = GameObject.FindWithTag("Player");
        DoorInteraction d = Object.FindObjectsByType<DoorInteraction>(FindObjectsInactive.Exclude)
            .FirstOrDefault(x => x.transform.parent != null && x.transform.parent.name == doorName);
        if (player == null || d == null) return;

        Transform door = d.transform.parent;
        Vector3 n = Flat(door.right);
        Vector3 a = d.transform.position + n * 4f, b = d.transform.position - n * 4f;
        LecternQuestion[] lecterns = Object.FindObjectsByType<LecternQuestion>(FindObjectsInactive.Include);
        float da = lecterns.Length > 0 ? lecterns.Min(l => Vector3.Distance(l.transform.position, a)) : 0f;
        float db = lecterns.Length > 0 ? lecterns.Min(l => Vector3.Distance(l.transform.position, b)) : 0f;
        Vector3 spot = da < db ? a : b;   // the side with the closer lectern is the room you come from
        Teleport(player, new Vector3(spot.x, player.transform.position.y, spot.z), d.transform.position);
    }

    static void GoToLectern()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null || RoomManager.Instance == null || RoomManager.Instance.CurrentRoom == null) return;
        LecternQuestion l = RoomManager.Instance.CurrentRoom.lectern;
        if (l == null) return;
        Vector3 toPlayer = Flat(RoomManager.Instance.CurrentRoom.checkpoint.position - l.transform.position);
        Vector3 spot = l.transform.position + toPlayer * 1.2f;
        Teleport(player, new Vector3(spot.x, player.transform.position.y, spot.z), l.transform.position);
    }

    static void StepThrough()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        DoorInteraction d = Object.FindObjectsByType<DoorInteraction>(FindObjectsInactive.Exclude)
            .OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
        if (d == null) return;
        Vector3 toDoor = Flat(d.transform.position - player.transform.position);
        Vector3 n = Flat(d.transform.parent.right);
        if (Vector3.Dot(n, toDoor) < 0) n = -n;
        Vector3 spot = d.transform.position + n * 5f;
        Teleport(player, new Vector3(spot.x, player.transform.position.y, spot.z), d.transform.position);
        PassageTrigger pt = d.transform.parent.GetComponentInChildren<PassageTrigger>();
        if (pt != null && pt.doorInteraction != null) pt.doorInteraction.PlayerCrossedDoor();
    }

    static void Teleport(GameObject player, Vector3 pos, Vector3 lookAt)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = pos;
        Vector3 dir = Flat(lookAt - pos);
        if (dir.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(dir);
        if (cc != null) cc.enabled = true;

        // Test only (play mode, resets when Play stops): look straight ahead and freeze mouse look
        // so clicking into the Game view doesn't spin the camera.
        foreach (MonoBehaviour mb in player.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "MouseMovement") continue;
            var t = mb.GetType();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            t.GetField("xRotation", flags)?.SetValue(mb, 0f);
            t.GetField("mouseSensitivity", flags)?.SetValue(mb, 0f);
            mb.transform.localRotation = Quaternion.identity;
        }
    }

    // ------------------------------------------------------------------ collision + camera fixes
    // The player's collision capsule was much thinner than its body (radius 0.25 vs 0.6 units), and the
    // camera sits near the capsule edge with a 0.3 near clip plane, so the view poked into walls and doors.
    static void FixPlayerCollision(GameObject player, string label, StringBuilder log)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            Vector3 ls = player.transform.lossyScale;
            float xz = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            Transform body = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Cylinder" && t.GetComponent<MeshFilter>() != null);
            float bodyRadius = body != null ? 0.5f * Mathf.Max(Mathf.Abs(body.lossyScale.x), Mathf.Abs(body.lossyScale.z)) : 0.6f;
            float target = bodyRadius / xz;                                         // local radius that matches the body
            float maxRadius = cc.height * Mathf.Abs(ls.y) / (2f * xz) - 0.01f;      // keep the capsule taller than wide
            target = Mathf.Min(target, maxRadius);
            if (Mathf.Abs(cc.radius - target) > 0.01f)
            {
                Undo.RecordObject(cc, "Player radius");
                log.AppendLine(label + ": CharacterController radius " + cc.radius.ToString("0.00") + " -> " + target.ToString("0.00") +
                               " (world " + (cc.radius * xz).ToString("0.00") + " -> " + (target * xz).ToString("0.00") + " units, same width as the body)");
                cc.radius = target;
            }
        }

        foreach (Camera cam in player.GetComponentsInChildren<Camera>(true))
            if (cam.nearClipPlane > 0.05f)
            {
                Undo.RecordObject(cam, "Near clip");
                log.AppendLine(label + ": camera near clip plane " + cam.nearClipPlane + " -> 0.05");
                cam.nearClipPlane = 0.05f;
            }
    }

    static void FixPlayerPrefab(StringBuilder log)
    {
        const string path = "Assets/Player.prefab";
        if (!File.Exists(path)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        var sb = new StringBuilder();
        FixPlayerCollision(root, "Player.prefab", sb);
        if (sb.Length > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); log.Append(sb); }
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void ApplyHorrorDoorMaterial(IEnumerable<DoorInteraction> doors, StringBuilder log)
    {
        TextureImporter ti = AssetImporter.GetAtPath(DoorHorrorTexturePath) as TextureImporter;
        if (ti == null) { Debug.LogWarning(Tag + "Horror door texture not found at " + DoorHorrorTexturePath); return; }
        if (ti.maxTextureSize != 1024 || !ti.sRGBTexture) { ti.maxTextureSize = 1024; ti.sRGBTexture = true; ti.SaveAndReimport(); }
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DoorHorrorTexturePath);

        Material horror = AssetDatabase.LoadAssetAtPath<Material>(DoorHorrorMaterialPath);
        int count = 0;
        foreach (DoorInteraction d in doors)
        {
            if (d == null || d.doorPivot == null) continue;
            foreach (MeshRenderer r in d.doorPivot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || m == horror) continue;
                    Texture main = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    if (main == null || main.name != "door_d") continue;   // only the door leaf texture (not knobs or frames)
                    if (horror == null)
                    {
                        horror = new Material(m) { name = "Door_Horror" };
                        AssetDatabase.CreateAsset(horror, DoorHorrorMaterialPath);
                        log.AppendLine("Created " + DoorHorrorMaterialPath + " (copy of the door material with a horror texture)");
                    }
                    mats[i] = horror;
                    changed = true;
                }
                if (!changed) continue;
                Undo.RecordObject(r, "Horror door");
                r.sharedMaterials = mats;
                count++;
            }
        }
        if (horror != null)
        {
            if (horror.HasProperty("_BaseMap")) horror.SetTexture("_BaseMap", tex);
            horror.mainTexture = tex;
            if (horror.HasProperty("_BaseColor")) horror.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.78f));
            if (horror.HasProperty("_Smoothness")) horror.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(horror);
        }
        if (count > 0) log.AppendLine("Horror door material applied to " + count + " door leaves (model, pivot and colliders unchanged)");
    }

    static void FitDoorLeafColliders(IEnumerable<DoorInteraction> doors, StringBuilder log)
    {
        foreach (DoorInteraction d in doors)
        {
            if (d == null || d.doorPivot == null) continue;
            foreach (BoxCollider bc in d.doorPivot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (bc.isTrigger) continue;
                MeshFilter mf = bc.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                if ((bc.center - mb.center).magnitude < 0.01f && (bc.size - mb.size).magnitude < 0.01f) continue;
                Undo.RecordObject(bc, "Fit door collider");
                log.AppendLine(DoorRoot(d).name + ": door leaf collider size " + bc.size.ToString("F2") + " -> mesh size " + mb.size.ToString("F2"));
                bc.center = mb.center;
                bc.size = mb.size;
            }
        }
    }

    static void SetLimit(LevelTimer timer, string scene, float seconds, StringBuilder log)
    {
        LevelTimeLimit l = timer.levelTimeLimits.FirstOrDefault(x => x.sceneName == scene);
        if (l == null) { l = new LevelTimeLimit { sceneName = scene }; timer.levelTimeLimits.Add(l); }
        if (Mathf.Abs(l.seconds - seconds) > 0.01f) log.AppendLine("Time limit " + scene + " = " + LevelTimer.Format(seconds));
        l.seconds = seconds;
    }

    // Result screen: room for a multi-line Congratulations message + a dark backdrop for the big screens.
    static void StyleResultCanvas(GameFlowManager gm, StringBuilder log)
    {
        if (gm.resultCanvas == null) return;
        Transform rc = gm.resultCanvas.transform;

        Image bd = rc.Find("Backdrop")?.GetComponent<Image>();
        if (bd == null)
        {
            bd = UIImage("Backdrop", rc, new Color(0f, 0f, 0f, 0.62f));
            Undo.RegisterCreatedObjectUndo(bd.gameObject, "Backdrop");
            Stretch(bd.rectTransform);
            bd.transform.SetAsFirstSibling();
            bd.gameObject.SetActive(false);
            log.AppendLine("Added a dark Backdrop behind the Congratulations / Game Over / Game Complete screens");
        }
        Undo.RecordObject(gm, "Backdrop");
        gm.resultBackdrop = bd;

        void Lay(TMP_Text t, Vector2 pos, Vector2 size)
        {
            if (t == null) return;
            Undo.RecordObject(t.rectTransform, "Result layout");
            Undo.RecordObject(t, "Result layout");
            t.rectTransform.anchoredPosition = pos;
            t.rectTransform.sizeDelta = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
        }
        Lay(gm.resultTitle, new Vector2(0, 80), new Vector2(900, 60));
        Lay(gm.resultDescription, new Vector2(0, -8), new Vector2(640, 120));

        if (gm.tryAgainButton != null)
        {
            RectTransform br = gm.tryAgainButton.GetComponent<RectTransform>();
            Undo.RecordObject(br, "Button layout");
            br.anchoredPosition = new Vector2(0, -100);
            br.sizeDelta = new Vector2(560, 40);
            TMP_Text label = gm.tryAgainButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Undo.RecordObject(label.rectTransform, "Button text");
                Undo.RecordObject(label, "Button text");
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }

    // ------------------------------------------------------------------ v5: vestibules, overlays, audio
    // A short corridor behind the door that turns out through a side exit (side = +1 right, -1 left).
    // No end wall in the way and nothing visible at the doorway: both passages of a room are identical.
    static DoorPassage CreatePassage(Transform parent, DoorInteraction d, Vector3 away, float side, bool leadsToNextRoom,
                                     float floorY, float ceilingY, Material wallMat, Material ceilMat, Material fireMat)
    {
        GameObject root = new GameObject("Passage (" + DoorRoot(d).name + ")");
        root.transform.SetParent(parent, false);
        Vector3 c = DoorOpeningCentre(d);
        root.transform.SetPositionAndRotation(new Vector3(c.x, floorY, c.z), Quaternion.LookRotation(away));
        Transform t = root.transform;

        float hw = OpeningHalfWidth + PassageMargin;
        float h = ceilingY - floorY + 0.2f;
        float y = h * 0.5f - 0.1f;
        float L = PassageDepth, W = PassageWall, s = side;
        float solidLen = L - ExitWidth;   // the turn-side wall stops where the side exit begins

        Block("Wall (far side)", t, new Vector3(-s * (hw + W * 0.5f), y, L * 0.5f), new Vector3(W, h, L + W), wallMat, new Vector2(L, h));
        Block("Wall (turn side)", t, new Vector3(s * (hw + W * 0.5f), y, (solidLen - W * 0.5f) * 0.5f), new Vector3(W, h, solidLen + W * 0.5f), wallMat, new Vector2(solidLen, h));
        Block("Exit Header", t, new Vector3(s * (hw + W * 0.5f), (ExitHeight + h - 0.1f) * 0.5f, L - ExitWidth * 0.5f),
              new Vector3(W, h - 0.1f - ExitHeight, ExitWidth), wallMat, new Vector2(ExitWidth, h - ExitHeight));
        Block("Wall (end)", t, new Vector3(0, y, L + W * 0.5f), new Vector3(2f * (hw + W), h, W), wallMat, new Vector2(2f * hw, h));
        Block("Ceiling", t, new Vector3(0, PassageCeiling + 0.15f, L * 0.5f), new Vector3(2f * hw, 0.3f, L), ceilMat, new Vector2(2f * hw, L));

        // dim, flickering old lamp
        GameObject lg = new GameObject("Corridor Lamp");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0, PassageCeiling - 1.2f, L * 0.5f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 16f; l.intensity = 7f; l.shadows = LightShadows.None;
        l.color = new Color(1f, 0.86f, 0.55f);

        // flames that fill the far end and the side exit (wrong door only)
        GameObject fireGo = new GameObject("Passage Fire");
        fireGo.transform.SetParent(t, false);
        fireGo.transform.localPosition = new Vector3(0, 0, L - ExitWidth * 0.5f);
        BoxCollider fb = fireGo.AddComponent<BoxCollider>();
        fb.isTrigger = true; fb.center = new Vector3(0, 3f, 0); fb.size = new Vector3(2f * hw, 6f, ExitWidth);
        var fires = new List<ParticleSystem>
        {
            CreateFire(fireGo.transform, new Vector3(-s * 2.4f, 0, 1.6f), fireMat, 0.85f),
            CreateFire(fireGo.transform, new Vector3(s * 0.8f, 0, 2.2f), fireMat, 0.9f),
            CreateFire(fireGo.transform, new Vector3(s * 3.4f, 0, -1.0f), fireMat, 0.9f),
            CreateFire(fireGo.transform, new Vector3(s * (hw + 1.2f), 0, 0.2f), fireMat, 1.0f),   // in the side exit
            CreateFire(fireGo.transform, new Vector3(-s * 1.0f, 0, -2.6f), fireMat, 0.7f),
        };
        GameObject flg = new GameObject("Fire Light");
        flg.transform.SetParent(fireGo.transform, false);
        flg.transform.localPosition = new Vector3(0, 3f, 0);
        Light fl = flg.AddComponent<Light>();
        fl.type = LightType.Point; fl.color = new Color(1f, 0.45f, 0.1f); fl.range = 18f; fl.intensity = 7f;
        fl.shadows = LightShadows.None; fl.enabled = false;
        FireHazard fh = fireGo.AddComponent<FireHazard>();
        fh.fireEffects = fires.ToArray();
        fh.fireLights = new[] { fl };
        fh.damagePlayer = false;   // the room system applies the wrong-answer damage

        // the side exit becomes impassable only while it is on fire
        GameObject blockGo = new GameObject("Fire Block");
        blockGo.transform.SetParent(t, false);
        blockGo.transform.localPosition = new Vector3(s * (hw + W * 0.5f), 5f, L - ExitWidth * 0.5f);
        BoxCollider block = blockGo.AddComponent<BoxCollider>();
        block.size = new Vector3(W + 0.6f, 10f, ExitWidth);
        block.enabled = false;

        DoorPassage dp = root.AddComponent<DoorPassage>();
        dp.door = d;
        dp.leadsToNextRoom = leadsToNextRoom;
        dp.fire = fh;
        dp.fireBlock = block;
        dp.corridorLight = l;

        Zone("Decide Zone", t, PassageZone.Kind.Decide, dp, new Vector3(0, 3f, DecideDepth), new Vector3(2f * hw, 6f, 1.2f));
        Zone("Exit Zone", t, PassageZone.Kind.Exit, dp, new Vector3(s * (hw + W + 1.5f), 3f, L - ExitWidth * 0.5f), new Vector3(2.5f, 6f, ExitWidth));
        return dp;
    }

    // Trigger just past a door; its transform is the door's frame (at the doorway, facing in).
    static DoorThreshold CreateThreshold(Transform parent, DoorInteraction d, Vector3 away, bool leadsToNextRoom, float floorY)
    {
        GameObject g = new GameObject("Threshold (" + DoorRoot(d).name + ")");
        g.transform.SetParent(parent, false);
        Vector3 c = DoorOpeningCentre(d);
        g.transform.SetPositionAndRotation(new Vector3(c.x, floorY, c.z), Quaternion.LookRotation(away));
        BoxCollider bc = g.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = new Vector3(0f, 4f, 1.6f);                       // just beyond the far face of the wall
        bc.size = new Vector3(OpeningHalfWidth * 2f, 8f, 1.8f);
        DoorThreshold t = g.AddComponent<DoorThreshold>();
        t.door = d;
        t.leadsToNextRoom = leadsToNextRoom;
        return t;
    }

    static Transform TopUnder(Transform group, Transform t)
    {
        while (t.parent != null && t.parent != group) t = t.parent;
        return t;
    }

    // Copies an object as pure decoration: same look and solid colliders, but no scripts,
    // no trigger zones, no UI and no sound, so nothing about it can be used.
    static GameObject CopyAsDecor(Transform src, Transform fromFrame, Transform toFrame, Transform parent, Vector3 extraOffset)
    {
        GameObject copy = Object.Instantiate(src.gameObject);
        copy.name = src.name + " (trap copy)";
        copy.transform.SetParent(parent, true);
        copy.transform.SetPositionAndRotation(
            toFrame.TransformPoint(fromFrame.InverseTransformPoint(src.position)) + extraOffset,
            toFrame.rotation * (Quaternion.Inverse(fromFrame.rotation) * src.rotation));
        copy.transform.localScale = src.lossyScale;

        foreach (Canvas cv in copy.GetComponentsInChildren<Canvas>(true))
            if (cv != null && cv.renderMode != RenderMode.WorldSpace) Object.DestroyImmediate(cv.gameObject);
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && !(mb is TMPro.TMP_Text)) Object.DestroyImmediate(mb);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true))
            if (c != null && c.isTrigger) Object.DestroyImmediate(c);
        foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true))
            Object.DestroyImmediate(a);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            if (t != null && (t.name.Contains("Sign") || t.name.Contains("Interaction") || t.name.Contains("Passage")))
                if (t != copy.transform) Object.DestroyImmediate(t.gameObject);
        copy.SetActive(true);
        return copy;
    }

    static void Zone(string name, Transform parent, PassageZone.Kind kind, DoorPassage dp, Vector3 localPos, Vector3 size)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        BoxCollider bc = g.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = size;
        PassageZone z = g.AddComponent<PassageZone>();
        z.kind = kind;
        z.passage = dp;
    }

    // Flames on the player for the wrong-door burn (child of the Player, off until needed).
    static void CreateBurnEffect(GameObject player, Material fireMat, StringBuilder log)
    {
        Transform old = player.transform.Find("Burn Effect");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);   // made by this tool, rebuilt
        GameObject root = new GameObject("Burn Effect");
        Undo.RegisterCreatedObjectUndo(root, "Burn effect");
        root.transform.SetParent(player.transform, false);
        Vector3 ls = player.transform.lossyScale;
        root.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);   // children in world units
        float feet = -player.transform.position.y + 0.1f;   // roughly the floor under the player
        var spots = new[] { new Vector3(0.9f, feet, 1.1f), new Vector3(-0.9f, feet, 1.0f), new Vector3(0f, feet, 1.6f), new Vector3(0.5f, feet, -0.6f), new Vector3(-0.6f, feet, -0.4f) };
        foreach (Vector3 sp in spots)
        {
            ParticleSystem ps = CreateFire(root.transform, sp, fireMat, 0.45f);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World;   // flames trail behind as you move
        }
        GameObject lg = new GameObject("Burn Light");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0, feet + 1.2f, 0.8f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.45f, 0.12f); l.range = 9f; l.intensity = 5f; l.shadows = LightShadows.None;
        l.enabled = false;
        log.AppendLine("Added 'Burn Effect' (flames + light) to the Player for the wrong-door burn");
    }

    static GameObject Block(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, Vector2 faceSize)
    {
        GameObject g = Block(name, parent, localPos, size, mat);
        // tile the room texture by real size instead of stretching it over the whole block
        var mpb = new MaterialPropertyBlock();
        Vector4 st = new Vector4(Mathf.Max(1f, faceSize.x / 7f), Mathf.Max(1f, faceSize.y / 7f), 0f, 0f);
        mpb.SetVector("_BaseMap_ST", st);
        mpb.SetVector("_MainTex_ST", st);
        g.GetComponent<Renderer>().SetPropertyBlock(mpb);
        return g;
    }

    static GameObject Block(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);   // has its own BoxCollider: solid
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        return g;
    }

    static Material GetOrCreateLitMaterial(string name, Color color, StringBuilder log)
    {
        string path = Root + "/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(Root + "/Materials");
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.SetColor("_BaseColor", color);
        m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine("Created material " + path);
        return m;
    }

    static void SetupOverlays(Transform hud, PlayerHealth health, GameFlowManager gm, StringBuilder log)
    {
        // Blood splatter (sits just above the red damage flash)
        Image blood = hud.Find("BloodOverlay")?.GetComponent<Image>();
        if (blood == null)
        {
            blood = UIImage("BloodOverlay", hud, new Color(1f, 1f, 1f, 0f));
            Undo.RegisterCreatedObjectUndo(blood.gameObject, "Blood");
            Stretch(blood.rectTransform);
            log.AppendLine("Created HUD BloodOverlay (blood splatter on every hit)");
        }
        TextureImporter ti = AssetImporter.GetAtPath(BloodTexturePath) as TextureImporter;
        if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single || !ti.alphaIsTransparency))
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;   // without this no Sprite is generated
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        Undo.RecordObject(blood, "Blood");
        blood.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BloodTexturePath);
        if (blood.sprite == null) Debug.LogWarning(Tag + "Blood splatter sprite not found at " + BloodTexturePath);
        blood.color = blood.sprite != null ? new Color(1f, 1f, 1f, 0f) : new Color(0.55f, 0f, 0f, 0f);
        blood.raycastTarget = false;
        blood.enabled = false;
        Transform dmg = hud.Find("DamageOverlay");
        blood.transform.SetSiblingIndex(dmg != null ? dmg.GetSiblingIndex() + 1 : 0);

        Undo.RecordObject(health, "Blood");
        health.bloodOverlay = blood;
        health.bloodDuration = 1.8f;
        health.overlayFadeSpeed = 0.6f;

        // Fade to black for the vestibule passages (top of the HUD)
        Image fade = hud.Find("Fade")?.GetComponent<Image>();
        if (fade == null)
        {
            fade = UIImage("Fade", hud, new Color(0f, 0f, 0f, 0f));
            Undo.RegisterCreatedObjectUndo(fade.gameObject, "Fade");
            Stretch(fade.rectTransform);
            log.AppendLine("Created HUD Fade image");
        }
        fade.color = new Color(0f, 0f, 0f, 0f);
        fade.gameObject.SetActive(false);
        fade.transform.SetAsLastSibling();

        Undo.RecordObject(gm, "Fade");
        gm.fadeOverlay = fade;
    }

    static void SetupAudio(GameObject session, GameFlowManager gm, Transform hud, StringBuilder log)
    {
        foreach (string f in Directory.GetFiles(AudioFolder, "*.wav"))
        {
            AudioImporter ai = AssetImporter.GetAtPath(f.Replace('\\', '/')) as AudioImporter;
            if (ai == null) continue;
            bool longClip = f.Contains("Ambience");
            AudioImporterSampleSettings ss = ai.defaultSampleSettings;
            AudioClipLoadType want = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            if (ss.loadType != want || ai.forceToMono != true)
            {
                ss.loadType = want;
                ai.defaultSampleSettings = ss;
                ai.forceToMono = true;
                ai.SaveAndReimport();
            }
        }

        AudioManager am = session.GetComponent<AudioManager>();
        if (am == null) { am = Undo.AddComponent<AudioManager>(session); log.AppendLine("Added AudioManager to Game Session"); }
        Undo.RecordObject(am, "Audio");
        am.backgroundMusic = Clip(AudioFolder + "/Horror_Ambience_Loop.wav");
        am.heartbeat = Clip(AudioFolder + "/Heartbeat_Loop.wav");
        am.tick = Clip(AudioFolder + "/Clock_Tick.wav");
        am.tickUrgent = Clip(AudioFolder + "/Clock_Tick_Urgent.wav");
        am.doorOpen = Clip(AudioFolder + "/Door_Open_Creak_Heavy.wav");   // heavy creak, timed to the door's swing
        am.doorClose = Clip(DoorAudioFolder + "/Door_Close.wav");
        am.doorCreak = null;   // the heavy creak replaces the old open + creak pair
        am.lecternApproach = Clip(AudioFolder + "/Lectern_Whisper.wav");
        am.lecternOpen = Clip(AudioFolder + "/Lectern_Open_Sting.wav");
        am.wrongAnswer = Clip(AudioFolder + "/Wrong_Answer_Horror_Sting.wav");
        am.fireLoop = Clip(AudioFolder + "/Fire_Burning_Loop.wav");
        am.passageOpen = Clip(AudioFolder + "/Door_Creak.wav");
        am.footstepsWalk = Enumerable.Range(1, 6).Select(n => Clip(AudioFolder + "/Footstep_Tile_Walk_" + n + ".wav")).Where(x => x != null).ToArray();
        am.footstepsRun = Enumerable.Range(1, 4).Select(n => Clip(AudioFolder + "/Footstep_Tile_Run_" + n + ".wav")).Where(x => x != null).ToArray();
        am.footstepVolume = 0.42f;   // audible, under the music
        am.correctAnswer = Clip(AudioFolder + "/Correct_Answer_Chime.wav");

        AudioMixer mixer = GetOrCreateMixer(log);
        am.mixer = mixer;
        am.musicGroup = mixer != null ? mixer.FindMatchingGroups("Music").FirstOrDefault() : null;
        am.sfxGroup = mixer != null ? mixer.FindMatchingGroups("SFX").FirstOrDefault() : null;
        if (mixer == null) log.AppendLine("AudioMixer could not be created automatically; volumes are applied on the AudioSources instead");

        // Any other AudioSource left in the scene that plays on its own would double the music
        foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            if (src.playOnAwake && src.clip != null && !src.transform.IsChildOf(session.transform))
                log.AppendLine("NOTE: '" + PathOf(src.transform) + "' also plays audio on start (left unchanged)");

        // Settings panel (Tab)
        Transform panelT = hud.Find("Audio Settings");
        if (panelT == null)
        {
            Image panel = UIImage("Audio Settings", hud, new Color(0.03f, 0.02f, 0.02f, 0.92f));
            Undo.RegisterCreatedObjectUndo(panel.gameObject, "Audio settings");
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 330));
            TMP_Text title = UIText("Title", panel.transform, "AUDIO SETTINGS", 40, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(700, 50));
            title.color = new Color(0.91f, 0.77f, 0.28f);
            TMP_Text music = UIText("Music Row", panel.transform, "MUSIC", 28, TextAlignmentOptions.Left, FontStyles.Bold);
            Place(music.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(680, 40));
            TMP_Text sfx = UIText("SFX Row", panel.transform, "SOUND EFFECTS", 28, TextAlignmentOptions.Left, FontStyles.Bold);
            Place(sfx.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -24), new Vector2(680, 40));
            TMP_Text help = UIText("Help", panel.transform, "UP / DOWN  choose     LEFT / RIGHT  change volume     TAB  close", 20, TextAlignmentOptions.Center, FontStyles.Normal);
            Place(help.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(720, 30));
            help.color = new Color(0.75f, 0.75f, 0.75f);
            panelT = panel.transform;
            log.AppendLine("Created the Audio Settings panel (Tab)");
        }
        panelT.SetAsLastSibling();
        panelT.gameObject.SetActive(false);

        Transform hint = hud.Find("Audio Hint");
        if (hint == null)
        {
            TMP_Text h = UIText("Audio Hint", hud, "TAB  Audio settings", 18, TextAlignmentOptions.Right, FontStyles.Normal);
            Undo.RegisterCreatedObjectUndo(h.gameObject, "Audio hint");
            Place(h.rectTransform, new Vector2(1, 0), new Vector2(-24, 18), new Vector2(320, 28));
            h.color = new Color(1f, 1f, 1f, 0.55f);
        }

        AudioSettingsMenu menu = session.GetComponent<AudioSettingsMenu>();
        if (menu == null) menu = Undo.AddComponent<AudioSettingsMenu>(session);
        Undo.RecordObject(menu, "Audio menu");
        menu.panel = panelT.gameObject;
        menu.musicRow = panelT.Find("Music Row")?.GetComponent<TMP_Text>();
        menu.sfxRow = panelT.Find("SFX Row")?.GetComponent<TMP_Text>();
        Undo.RecordObject(gm, "Audio menu");
        gm.settingsMenu = menu;
        log.AppendLine("AudioManager: 11 clips assigned, mixer " + (mixer != null ? "connected (Music / SFX groups)" : "not used"));
    }

    static AudioClip Clip(string path)
    {
        AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null) Debug.LogWarning(Tag + "Audio clip not found: " + path);
        return c;
    }

    // Unity has no public API to build an AudioMixer from code, so this uses the editor's own
    // (internal) AudioMixerController. If that ever fails, the game still works without a mixer.
    static AudioMixer GetOrCreateMixer(StringBuilder log)
    {
        AudioMixer existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (existing != null && existing.FindMatchingGroups("Music").Length > 0 && existing.FindMatchingGroups("SFX").Length > 0)
            return existing;
        if (existing != null) AssetDatabase.DeleteAsset(MixerPath);   // incomplete one made by this tool

        try
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
            System.Reflection.Assembly asm = typeof(EditorWindow).Assembly;
            System.Type ctrlT = asm.GetType("UnityEditor.Audio.AudioMixerController");
            System.Type groupT = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            System.Type pathT = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            object ctrl = ctrlT.GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { MixerPath });
            object master = ctrlT.GetProperty("masterGroup", F).GetValue(ctrl);

            var exposeNames = new List<(object guid, string name)>();
            foreach (string gname in new[] { "Music", "SFX" })
            {
                object g = ctrlT.GetMethod("CreateNewGroup", F).Invoke(ctrl, new object[] { gname, false });
                ctrlT.GetMethod("AddChildToParent", F).Invoke(ctrl, new[] { g, master });
                ctrlT.GetMethod("AddGroupToCurrentView", F)?.Invoke(ctrl, new[] { g });
                object guid = groupT.GetMethod("GetGUIDForVolume", F).Invoke(g, null);
                object ppath = System.Activator.CreateInstance(pathT, F, null, new[] { g, guid }, null);
                ctrlT.GetMethod("AddExposedParameter", F).Invoke(ctrl, new[] { ppath });
                exposeNames.Add((guid, gname == "Music" ? "MusicVolume" : "SFXVolume"));
            }

            System.Reflection.PropertyInfo expP = ctrlT.GetProperty("exposedParameters", F);
            System.Array arr = (System.Array)expP.GetValue(ctrl);
            for (int i = 0; i < arr.Length; i++)
            {
                object e = arr.GetValue(i);
                System.Reflection.FieldInfo gf = e.GetType().GetField("guid", F), nf = e.GetType().GetField("name", F);
                foreach (var x in exposeNames)
                    if (gf.GetValue(e).Equals(x.guid)) nf.SetValue(e, x.name);
                arr.SetValue(e, i);
            }
            expP.SetValue(ctrl, arr);
            ctrlT.GetMethod("OnSubAssetChanged", F)?.Invoke(ctrl, null);

            EditorUtility.SetDirty((Object)ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath);
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            log.AppendLine("Created AudioMixer " + MixerPath + " (groups Music + SFX, exposed MusicVolume + SFXVolume)");
            return mixer;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(Tag + "AudioMixer creation failed, using direct volumes instead: " + ex.GetBaseException().Message);
            if (AssetDatabase.LoadAssetAtPath<Object>(MixerPath) != null) AssetDatabase.DeleteAsset(MixerPath);
            return null;
        }
    }

    // ------------------------------------------------------------------ builders
    static QuestionBank GetOrCreateQuestionBank(StringBuilder log)
    {
        QuestionBank bank = AssetDatabase.LoadAssetAtPath<QuestionBank>(QuestionBankPath);
        if (bank != null) return bank;

        Directory.CreateDirectory(Root + "/Data");
        bank = ScriptableObject.CreateInstance<QuestionBank>();
        void Q(string q, string a, string b, AnswerLetter c) =>
            bank.questions.Add(new QuizQuestion { question = q, answerA = a, answerB = b, correctAnswer = c });

        Q("What is an Algorithm?", "A step-by-step procedure for solving a problem.", "A device used to run programs.", AnswerLetter.A);
        Q("What happens when a loop has no stopping condition?", "It may run continuously.", "It automatically stops.", AnswerLetter.A);
        Q("Which operator means NOT EQUAL?", "==", "!=", AnswerLetter.B);
        Q("What is the purpose of a Function?", "To organize reusable blocks of code.", "To permanently delete data.", AnswerLetter.A);
        Q("What is the result of 10 + 5 * 2?", "30", "20", AnswerLetter.B);
        Q("What is an Infinite Loop?", "A loop that continues without stopping.", "A loop that runs only once.", AnswerLetter.A);
        Q("Which is an example of a Boolean value?", "True", "25", AnswerLetter.A);
        Q("What is the purpose of a Conditional Statement?", "To make decisions based on conditions.", "To store multiple pieces of information.", AnswerLetter.A);
        Q("What is the output of this code?\nx = 8; x = x + 2; print(x)", "8", "10", AnswerLetter.B);
        Q("What is the purpose of Debugging?", "To find and fix errors in a program.", "To make a computer run without electricity.", AnswerLetter.A);
        Q("Which data structure stores an ordered collection of elements?", "Array", "Boolean", AnswerLetter.A);
        Q("What does a Compiler do?", "Translates source code into executable form.", "Deletes unnecessary files.", AnswerLetter.A);
        Q("What is the result of 20 % 6?", "2", "3", AnswerLetter.A);
        Q("What is the purpose of a Comment in programming?", "To explain or describe the code.", "To execute additional commands.", AnswerLetter.A);
        Q("What is a Syntax Error?", "When code violates the writing rules of a programming language.", "When a program automatically fixes itself.", AnswerLetter.A);

        AssetDatabase.CreateAsset(bank, QuestionBankPath);
        AssetDatabase.SaveAssets();
        log.AppendLine("Created " + QuestionBankPath + " with the 15 Level 1 questions");
        return bank;
    }

    static AnswerSign GetOrCreateSign(DoorInteraction d, Vector3 towardRoom, Material panelMat, Material frameMat, StringBuilder log)
    {
        Transform doorRoot = DoorRoot(d);

        // Height: centre of the beam above the doorway. Depth: just in front of the beam's face on the room side
        // (measured from the beam itself, because the beam is not exactly centred on the door).
        Transform beam = doorRoot.Find("Cube (23)");
        Vector3 c = DoorOpeningCentre(d);
        float y = 12.4f;
        Vector3 facePoint = c + towardRoom * 0.66f;
        if (beam != null && beam.GetComponent<Renderer>() is Renderer br)
        {
            y = br.bounds.center.y;
            float half = Mathf.Abs(Vector3.Dot(br.bounds.extents, new Vector3(Mathf.Abs(towardRoom.x), 0, Mathf.Abs(towardRoom.z))));
            facePoint = br.bounds.center + towardRoom * (half + 0.08f);
        }
        // keep the sign centred on the doorway, but on the beam's front face
        Vector3 along = Vector3.Cross(Vector3.up, towardRoom);
        Vector3 pos = facePoint + along * Vector3.Dot(c - facePoint, along);
        pos.y = y;
        Quaternion rot = Quaternion.LookRotation(-towardRoom);   // faces the room

        AnswerSign existing = doorRoot.GetComponentInChildren<AnswerSign>(true);
        if (existing != null)
        {
            Undo.RecordObject(existing.transform, "Sign position");
            existing.transform.SetPositionAndRotation(pos, rot);
            return existing;
        }

        GameObject root = new GameObject("Answer Sign");
        Undo.RegisterCreatedObjectUndo(root, "Sign");
        root.transform.SetParent(doorRoot, true);
        root.transform.SetPositionAndRotation(pos, rot);

        Quad("Frame", root.transform, new Vector3(0, 0, 0.03f), new Vector2(7.3f, 3.0f), frameMat);
        Quad("Panel", root.transform, new Vector3(0, 0, 0.02f), new Vector2(7.0f, 2.7f), panelMat);
        Quad("Divider", root.transform, new Vector3(-2.15f, 0, 0.01f), new Vector2(0.05f, 2.2f), frameMat);

        TextMeshPro letter = WorldText("Letter", root.transform, new Vector3(-2.85f, 0, 0), new Vector2(1.3f, 2.4f), 16f, FontStyles.Bold);
        letter.text = "A";
        TextMeshPro answer = WorldText("Answer", root.transform, new Vector3(0.68f, 0, 0), new Vector2(5.2f, 2.4f), 5f, FontStyles.Normal);
        answer.enableAutoSizing = true; answer.fontSizeMin = 2.2f; answer.fontSizeMax = 5f;
        answer.alignment = TextAlignmentOptions.MidlineLeft;
        answer.text = "Answer";

        AnswerSign sign = root.AddComponent<AnswerSign>();
        sign.letterText = letter;
        sign.answerText = answer;
        log.AppendLine("Added answer sign above " + doorRoot.name);
        return sign;
    }

    static void Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Material mat)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Object.DestroyImmediate(q.GetComponent<Collider>());   // signs never block anything
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localPos;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        Renderer r = q.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static TextMeshPro WorldText(string name, Transform parent, Vector3 localPos, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        t.rectTransform.sizeDelta = size;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.color = new Color(0.95f, 0.91f, 0.82f);
        return t;
    }

    static Material GetOrCreateUnlitMaterial(string name, Color color, StringBuilder log)
    {
        string path = Root + "/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(Root + "/Materials");
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        m = new Material(sh);
        m.SetColor("_BaseColor", color);
        m.color = color;
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine("Created material " + path);
        return m;
    }

    static void CreateLamp(Transform parent, string label, Vector3 cellCentre, float ceilingY, GameObject lampPrefab, float intensity, float range)
    {
        GameObject root = new GameObject("Ceiling Lamp (" + label + ")");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(cellCentre.x, ceilingY, cellCentre.z);

        if (lampPrefab != null)
        {
            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, root.transform);
            lamp.transform.localPosition = new Vector3(0, -0.02f, 0);
            lamp.transform.localRotation = new Quaternion(0.5f, -0.5f, 0.5f, 0.5f);   // same as the reference
            lamp.transform.localScale = Vector3.one * 9.841425f;
        }

        GameObject lg = new GameObject("Point Light");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0, -3.3f, 0);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.98688287f, 0.54905653f);   // reference colour
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    static Transform CreateHud(Transform parent, PlayerHealth health, LevelTimer timer, StringBuilder log)
    {
        GameObject hud = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(hud, "HUD");
        hud.transform.SetParent(parent, false);
        Canvas cv = hud.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler cs = hud.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        Image overlay = UIImage("DamageOverlay", hud.transform, new Color(0.8f, 0.05f, 0f, 0f));
        Stretch(overlay.rectTransform);

        Image panel = UIImage("HealthBar", hud.transform, new Color(0f, 0f, 0f, 0.55f));
        Place(panel.rectTransform, new Vector2(0, 1), new Vector2(40, -40), new Vector2(420, 70));
        TMP_Text hLabel = UIText("Label", panel.transform, "HEALTH", 22, TextAlignmentOptions.Left, FontStyles.Bold);
        Place(hLabel.rectTransform, new Vector2(0, 1), new Vector2(16, -6), new Vector2(200, 28));
        TMP_Text hValue = UIText("Value", panel.transform, "100 / 100", 22, TextAlignmentOptions.Right, FontStyles.Bold);
        Place(hValue.rectTransform, new Vector2(1, 1), new Vector2(-16, -6), new Vector2(200, 28));
        Image track = UIImage("Track", panel.transform, new Color(0.18f, 0.18f, 0.18f, 1f));
        RectTransform tr = track.rectTransform;
        tr.anchorMin = new Vector2(0, 0); tr.anchorMax = new Vector2(1, 0); tr.pivot = new Vector2(0.5f, 0);
        tr.offsetMin = new Vector2(16, 12); tr.offsetMax = new Vector2(-16, 32);
        Image fill = UIImage("Fill", track.transform, new Color(0.26f, 0.79f, 0.47f));
        Stretch(fill.rectTransform);

        Image tPanel = UIImage("Timer", hud.transform, new Color(0f, 0f, 0f, 0.55f));
        Place(tPanel.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(220, 90));
        TMP_Text tLabel = UIText("Label", tPanel.transform, "TIME LEFT", 18, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(tLabel.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(200, 24));
        TMP_Text tValue = UIText("Value", tPanel.transform, "05:00", 46, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(tValue.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(200, 56));

        health.healthFill = fill; health.healthText = hValue; health.damageOverlay = overlay;
        timer.timerText = tValue;
        log.AppendLine("Created HUD Canvas (health bar + timer)");
        return hud.transform;
    }

    // Moves a component (keeping its settings) from wherever it is onto the target object.
    static T MoveComponentTo<T>(Scene scene, GameObject target, StringBuilder log) where T : Component
    {
        T onTarget = target.GetComponent<T>();
        T elsewhere = All<T>(scene).FirstOrDefault(x => x != onTarget);
        if (onTarget == null)
        {
            onTarget = Undo.AddComponent<T>(target);
            if (elsewhere != null)
            {
                EditorUtility.CopySerialized(elsewhere, onTarget);
                log.AppendLine("Moved " + typeof(T).Name + " from " + elsewhere.name + " to " + target.name + " (settings kept)");
            }
        }
        foreach (T extra in All<T>(scene).Where(x => x != onTarget).ToArray())
            Undo.DestroyObjectImmediate(extra);
        return onTarget;
    }

    static FireHazard CreateFireRoom(Transform hazardGroup, DoorInteraction trap, Vector2Int cell, Vector3 c, float floorY, float ceilingY, Material fireMat, StringBuilder log)
    {
        GameObject hazardGo = new GameObject("Fire Room (" + DoorRoot(trap).name + ")");
        Undo.RegisterCreatedObjectUndo(hazardGo, "Create fire");
        hazardGo.transform.SetParent(hazardGroup, false);
        hazardGo.transform.position = new Vector3(c.x, floorY, c.z);

        BoxCollider box = hazardGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        float h = Mathf.Max(4f, ceilingY - floorY);
        box.center = new Vector3(0, h * 0.5f, 0);
        box.size = new Vector3(RoomSize - 1f, h, RoomSize - 1f);

        Vector3 toDoor = Flat(DoorRoot(trap).position - c);
        Vector3 side = Vector3.Cross(Vector3.up, toDoor);
        var spots = new List<Vector3>
        {
            -toDoor * 10f - side * 9f, -toDoor * 10f, -toDoor * 10f + side * 9f,
            -side * 10f, side * 10f, toDoor * 4f - side * 10f, toDoor * 4f + side * 10f, Vector3.zero
        };
        var fires = spots.Select((s, i) => CreateFire(hazardGo.transform, s, fireMat, i == spots.Count - 1 ? 1.4f : 1f)).ToList();

        GameObject lightGo = new GameObject("Fire Light");
        lightGo.transform.SetParent(hazardGo.transform, false);
        lightGo.transform.localPosition = new Vector3(0, 4f, 0);
        Light fl = lightGo.AddComponent<Light>();
        fl.type = LightType.Point; fl.color = new Color(1f, 0.45f, 0.1f); fl.range = RoomSize * 0.9f; fl.intensity = 6f;
        fl.shadows = LightShadows.None; fl.enabled = false;

        FireHazard fh = hazardGo.AddComponent<FireHazard>();
        fh.fireEffects = fires.ToArray();
        fh.fireLights = new[] { fl };
        log.AppendLine("Added fire room behind " + DoorRoot(trap).name);
        return fh;
    }

    static ParticleSystem CreateFire(Transform parent, Vector3 localPos, Material mat, float scale)
    {
        GameObject go = new GameObject("Fire");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * scale, 4.5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(1.6f * scale, 3.2f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.45f, 0.08f));
        main.gravityModifier = -0.15f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 300;
        var em = ps.emission; em.rateOverTime = 45f * scale;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 1.4f * scale;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.35f), new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.8f; noise.scrollSpeed = 1f;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    static Material GetOrCreateFireMaterial(StringBuilder log)
    {
        string matPath = Root + "/Materials/Fire_Additive.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;
        Directory.CreateDirectory(Root + "/Materials");
        Material m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        AssetDatabase.CreateAsset(m, matPath);
        log.AppendLine("Created fire material " + matPath);
        return m;
    }

    static void EnsureLevelsInBuild(StringBuilder log)
    {
        var list = EditorBuildSettings.scenes.ToList();
        bool changed = false;
        foreach (string lvl in new[] { "Level 1", "Level 2", "Level 3" })
        {
            string path = "Assets/Scenes/" + lvl + ".unity";
            if (!File.Exists(path) || list.Any(s => s.path == path)) continue;
            list.Add(new EditorBuildSettingsScene(path, true));
            changed = true;
            log.AppendLine("Added " + lvl + " to the build scene list");
        }
        if (changed) EditorBuildSettings.scenes = list.ToArray();
    }

    static Transform DoorRoot(DoorInteraction d) => d.transform.parent != null ? d.transform.parent : d.transform;

    // The InteractionZone sits in the middle of the doorway.
    static Vector3 DoorOpeningCentre(DoorInteraction d) => d.transform.position;

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 0 ? v.normalized : Vector3.forward; }

    static T[] All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    static GameObject FindRoot(Scene scene, string name)
    {
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
        if (root != null) return root;
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Select(t => t.gameObject).FirstOrDefault(g => g.CompareTag(name));
    }

    static Transform Group(Scene scene, string name)
    {
        GameObject g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
        if (g == null)
        {
            g = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(g, "Create group");
        }
        return g.transform;
    }

    static void Move(GameObject go, Transform parent, StringBuilder log)
    {
        if (go.transform == parent || go.transform.parent == parent) return;
        Undo.SetTransformParent(go.transform, parent, true, "Organise");
        log.AppendLine("Moved " + go.name + " into " + parent.name);
    }

    static Bounds BoundsOf(Transform parent, params string[] childNames)
    {
        bool has = false;
        Bounds b = new Bounds();
        foreach (string n in childNames)
        {
            Transform t = parent.Find(n);
            if (t == null) continue;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        return has ? b : new Bounds(Vector3.zero, Vector3.zero);
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    static Image UIImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text UIText(string name, Transform parent, string text, float size, TextAlignmentOptions align, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.fontStyle = style;
        t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
