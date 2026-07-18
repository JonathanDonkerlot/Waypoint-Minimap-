using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Alta.Map;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Main
{
    public sealed class MinimapMod
    {
        private const float ScreenMapMinSize = 180f;
        private const float ScreenMapMaxSize = 900f;
        private const float ScreenMapLocalMinX = -1.0628f;
        private const float ScreenMapLocalMaxX = 1.195f;
        private const float ScreenMapLocalMinY = 0.6489f;
        private const float ScreenMapLocalMaxY = 2.6891f;
        private const float ControlsHeight = 210f;
        private const float MaskOverlayAlpha = 1f;
        private const int TownshipMapPieceCount = 17;
        private const int TownshipUnusedLockedPieceTolerance = 2;

        private static readonly Vector2 ScreenMapRefA = new Vector2(-0.7f, 1f);
        private static readonly Vector2 ScreenMapRefB = new Vector2(0f, 2.3f);
        private static readonly Vector2 ScreenMapRefC = new Vector2(1f, 1.3f);
        private static readonly Vector2 ScreenMapWorldRefA = new Vector2(-1425.5f, -347.3f);
        private static readonly Vector2 ScreenMapWorldRefB = new Vector2(-849.7f, 737.1f);
        private static readonly Vector2 ScreenMapWorldRefC = new Vector2(62.2f, -102.2f);
        private readonly List<ScreenMapPlayer> _screenMapPlayers = new List<ScreenMapPlayer>();
        private readonly List<ScreenMapMask> _screenMapMasks = new List<ScreenMapMask>();
        private Rect _screenMapWindow = new Rect(340f, 20f, 320f, 530f);
        private Vector2 _menuScroll;
        private MapBoard _screenMapBoard;
        private Texture2D _screenMapTexture;
        private string _screenMapStatus = "Press Refresh Players or Reload Map Background.";
        private string _screenMapBackgroundStatus = "map not loaded";
        private string _screenMapMaskStatus = "covers not scanned";
        private float _screenMapSize = 300f;
        private float _screenMapMarkerOffsetX = -5f;
        private float _screenMapMarkerOffsetY = -42f;
        private bool _screenMapBackgroundLocked = true;
        private bool _mapOpen;
        private bool _savedCursorStateForMap;
        private CursorLockMode _savedCursorLockState;
        private bool _savedCursorVisible;

        public bool IsOpen => _mapOpen;

        public void Init()
        {
            MelonLogger.Msg("[Minimap] Loaded. Press M to open or close the minimap.");
        }

        public void OnUpdate()
        {
            if (InputHelper.GetKeyDown(Key.M))
            {
                SetMapOpen(!_mapOpen);
            }

            if (_mapOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void OnGUI()
        {
            if (!_mapOpen)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _screenMapWindow.width = _screenMapSize;
            _screenMapWindow.height = _screenMapSize + ControlsHeight;
            _screenMapWindow = GUI.Window(88323, _screenMapWindow, DrawScreenMapWindow, "Minimap");
        }

        public void OnDeinitializeMelon()
        {
            RestoreCursorState();
            if (_screenMapTexture != null)
            {
                UnityEngine.Object.Destroy(_screenMapTexture);
                _screenMapTexture = null;
            }
        }

        private void DrawScreenMapWindow(int id)
        {
            var mapRect = new Rect(10f, 26f, _screenMapSize - 20f, _screenMapSize - 20f);
            GUI.Box(mapRect, GUIContent.none);

            var oldColor = GUI.color;
            if (_screenMapTexture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(mapRect, _screenMapTexture, ScaleMode.StretchToFill, false);
            }
            else
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(mapRect, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(new Rect(mapRect.center.x - 1f, mapRect.y, 2f, mapRect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(mapRect.x, mapRect.center.y - 1f, mapRect.width, 2f), Texture2D.whiteTexture);
            }

            GUI.color = oldColor;
            DrawScreenMapMasks(mapRect, _screenMapMasks);
            DrawScreenMapPlayers(mapRect, _screenMapBoard, _screenMapPlayers, _screenMapMasks, new Vector2(_screenMapMarkerOffsetX, _screenMapMarkerOffsetY));
            DrawScreenMapWaypoints(mapRect, _screenMapMasks, new Vector2(_screenMapMarkerOffsetX, _screenMapMarkerOffsetY));

            GUILayout.BeginArea(new Rect(10f, _screenMapSize + 8f, _screenMapSize - 20f, ControlsHeight - 16f));
            _menuScroll = GUILayout.BeginScrollView(_menuScroll);
            GUILayout.Label(_screenMapStatus);

            GUILayout.Label("Waypoints: " + WaypointManager.GetWaypointsForDisplay().Count);

            if (GUILayout.Button("Refresh Players"))
            {
                _screenMapStatus = RefreshScreenMapPlayers();
            }

            if (GUILayout.Button("Reload Map Background"))
            {
                RefreshScreenMapBackground(true);
                RefreshScreenMapMasks();
                _screenMapStatus = BuildScreenMapStatus("manual");
            }

            if (GUILayout.Button("Refresh All"))
            {
                _screenMapStatus = RefreshScreenMapSnapshot(true);
            }

            _screenMapBackgroundLocked = GUILayout.Toggle(_screenMapBackgroundLocked, "Lock Map Background");
            GUILayout.Label("Size: " + _screenMapSize.ToString("0"));
            _screenMapSize = GUILayout.HorizontalSlider(_screenMapSize, ScreenMapMinSize, ScreenMapMaxSize);
            GUILayout.Label("Marker X Offset: " + _screenMapMarkerOffsetX.ToString("0"));
            _screenMapMarkerOffsetX = GUILayout.HorizontalSlider(_screenMapMarkerOffsetX, -60f, 60f);
            GUILayout.Label("Marker Y Offset: " + _screenMapMarkerOffsetY.ToString("0"));
            _screenMapMarkerOffsetY = GUILayout.HorizontalSlider(_screenMapMarkerOffsetY, -60f, 60f);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.DragWindow(new Rect(0f, 0f, _screenMapSize, 22f));
        }
        private void DrawScreenMapWaypoints(Rect mapRect, List<ScreenMapMask> masks, Vector2 markerOffset)
        {
            var waypointData = WaypointManager.GetWaypointsForDisplay();
            if (waypointData.Count == 0)
            {
                return;
            }

            var localPosition = FindLocalScreenMapPosition(_screenMapPlayers);

            foreach (var (worldPosition, color) in waypointData)
            {
                var screenPoint = CalibratedWorldToScreen(worldPosition, mapRect);
                if (!screenPoint.HasValue && _screenMapBoard != null)
                {
                    screenPoint = MapBoardWorldToScreen(_screenMapBoard, worldPosition, mapRect);
                }
                if (!screenPoint.HasValue)
                {
                    screenPoint = RadarWorldToScreen(worldPosition, localPosition, mapRect);
                }
                if (!screenPoint.HasValue)
                {
                    continue;
                }

                var markerPoint = screenPoint.Value + markerOffset;

                if (IsScreenPointCovered(markerPoint, mapRect, masks))
                {
                    continue;
                }

                DrawScreenMapDot(markerPoint, color);
            }
        }

        private void SetMapOpen(bool open)
        {
            if (_mapOpen == open)
            {
                return;
            }

            _mapOpen = open;
            if (open)
            {
                SaveCursorState();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _screenMapStatus = RefreshScreenMapSnapshot(false);
                return;
            }

            RestoreCursorState();
        }

        private void SaveCursorState()
        {
            if (_savedCursorStateForMap)
            {
                return;
            }

            _savedCursorLockState = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;
            _savedCursorStateForMap = true;
        }

        private void RestoreCursorState()
        {
            if (!_savedCursorStateForMap)
            {
                return;
            }

            Cursor.lockState = _savedCursorLockState;
            Cursor.visible = _savedCursorVisible;
            _savedCursorStateForMap = false;
        }

        private string RefreshScreenMapSnapshot(bool replaceExisting)
        {
            RefreshScreenMapBackground(replaceExisting);
            RefreshScreenMapMasks();
            return RefreshScreenMapPlayers();
        }

        private string RefreshScreenMapPlayers()
        {
            _screenMapPlayers.Clear();

            var source = AddScreenMapPlayersFromRoster(_screenMapPlayers);
            if (_screenMapPlayers.Count == 0)
            {
                AddScreenMapPlayersFromCurrentPlayer(_screenMapPlayers);
                source = _screenMapPlayers.Count > 0 ? "Player.Current" : source;
            }

            return BuildScreenMapStatus(source);
        }

        private void RefreshScreenMapMasks()
        {
            _screenMapMasks.Clear();
            _screenMapMaskStatus = AddScreenMapMasksFromScene(_screenMapMasks);
        }

        private void RefreshScreenMapBackground(bool replaceExisting)
        {
            var board = FindBestMapBoard();
            if (board != null)
            {
                _screenMapBoard = board;
            }

            if (_screenMapBackgroundLocked && _screenMapTexture != null && IsUsableScreenMapTexture(_screenMapTexture))
            {
                _screenMapBackgroundStatus = "locked snapshot " + _screenMapTexture.width.ToString() + "x" + _screenMapTexture.height.ToString();
                return;
            }

            if (!replaceExisting && _screenMapTexture != null && IsUsableScreenMapTexture(_screenMapTexture))
            {
                _screenMapBackgroundStatus = "kept texture";
                return;
            }

            var texture = FindScreenMapTexture();
            var snapshot = CreateScreenMapSnapshot(texture);
            if (snapshot != null)
            {
                if (_screenMapTexture != null)
                {
                    UnityEngine.Object.Destroy(_screenMapTexture);
                }

                _screenMapTexture = snapshot;
                _screenMapBackgroundStatus = "snapshot " + snapshot.width.ToString() + "x" + snapshot.height.ToString();
                return;
            }

            _screenMapBackgroundStatus = _screenMapTexture != null && IsUsableScreenMapTexture(_screenMapTexture)
                ? "kept old texture"
                : "waiting for discovered map render";
        }

        private string BuildScreenMapStatus(string source)
        {
            var boardText = _screenMapBoard != null ? "board" : "no board";
            return "Minimap: " + _screenMapPlayers.Count + " players, " + WaypointManager.GetWaypointsForDisplay().Count + " waypoints, " + _screenMapMasks.Count + " covers, " + _screenMapMaskStatus + ", " + boardText + ", " + _screenMapBackgroundStatus + ", " + source + ".";
        }

        private static void DrawScreenMapMasks(Rect mapRect, List<ScreenMapMask> masks)
        {
            if (masks == null || masks.Count == 0)
            {
                return;
            }

            var oldColor = GUI.color;
            for (var i = 0; i < masks.Count; i++)
            {
                var mask = masks[i];
                var rect = new Rect(
                    mapRect.x + mask.NormalizedRect.xMin * mapRect.width,
                    mapRect.y + mask.NormalizedRect.yMin * mapRect.height,
                    mask.NormalizedRect.width * mapRect.width,
                    mask.NormalizedRect.height * mapRect.height);

                if (rect.width <= 1f || rect.height <= 1f)
                {
                    continue;
                }

                GUI.color = new Color(0f, 0f, 0f, MaskOverlayAlpha);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
            }

            GUI.color = oldColor;
        }

        private static void DrawScreenMapPlayers(Rect mapRect, MapBoard board, List<ScreenMapPlayer> players, List<ScreenMapMask> masks, Vector2 markerOffset)
        {
            if (players == null || players.Count == 0)
            {
                return;
            }

            var localPosition = FindLocalScreenMapPosition(players);
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var livePosition = player.SourceObject != null ? GetPlayerWorldPosition(player.SourceObject) : null;
                if (livePosition.HasValue)
                {
                    player.Position = livePosition.Value;
                }

                var screenPoint = CalibratedWorldToScreen(player.Position, mapRect);
                if (!screenPoint.HasValue && board != null)
                {
                    screenPoint = MapBoardWorldToScreen(board, player.Position, mapRect);
                }
                if (!screenPoint.HasValue)
                {
                    screenPoint = RadarWorldToScreen(player.Position, localPosition, mapRect);
                }
                if (!screenPoint.HasValue)
                {
                    continue;
                }

                if (IsScreenPointCovered(screenPoint.Value, mapRect, masks))
                {
                    continue;
                }

                var markerPoint = screenPoint.Value + markerOffset;
                DrawScreenMapDot(markerPoint, player.IsLocal ? Color.yellow : Color.red);
                GUI.color = player.IsLocal ? Color.yellow : Color.red;
                GUI.Label(new Rect(markerPoint.x - 74f, markerPoint.y - 38f, 148f, 34f), player.Label);
                GUI.color = Color.white;
            }
        }

        private static void DrawScreenMapDot(Vector2 point, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(point.x - 4f, point.y - 4f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private static bool IsScreenPointCovered(Vector2 point, Rect mapRect, List<ScreenMapMask> masks)
        {
            if (masks == null || masks.Count == 0 || mapRect.width <= 0.001f || mapRect.height <= 0.001f)
            {
                return false;
            }

            var normalized = new Vector2(
                Mathf.Clamp01((point.x - mapRect.x) / mapRect.width),
                Mathf.Clamp01((point.y - mapRect.y) / mapRect.height));

            for (var i = 0; i < masks.Count; i++)
            {
                if (masks[i].NormalizedRect.Contains(normalized))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2? MapBoardWorldToScreen(MapBoard board, Vector3 worldPosition, Rect mapRect)
        {
            try
            {
                var mapPosition = board.WorldToMapPosition(new Vector2(worldPosition.x, worldPosition.z));
                var resolution = GetMapBoardResolution(board);
                if (resolution.x <= 0.001f || resolution.y <= 0.001f)
                {
                    return null;
                }

                var normalizedX = Mathf.Abs(mapPosition.x) <= 1.5f ? mapPosition.x : mapPosition.x / resolution.x;
                var normalizedY = Mathf.Abs(mapPosition.y) <= 1.5f ? mapPosition.y : mapPosition.y / resolution.y;
                var x = mapRect.x + Mathf.Clamp01(normalizedX) * mapRect.width;
                var y = mapRect.y + Mathf.Clamp01(normalizedY) * mapRect.height;
                return new Vector2(x, y);
            }
            catch
            {
                return null;
            }
        }

        private static Vector2? CalibratedWorldToScreen(Vector3 worldPosition, Rect mapRect)
        {
            var world = new Vector2(worldPosition.x, worldPosition.z);
            var basisB = ScreenMapWorldRefB - ScreenMapWorldRefA;
            var basisC = ScreenMapWorldRefC - ScreenMapWorldRefA;
            var delta = world - ScreenMapWorldRefA;
            var determinant = basisB.x * basisC.y - basisB.y * basisC.x;
            if (Mathf.Abs(determinant) < 0.001f)
            {
                return null;
            }

            var u = (delta.x * basisC.y - delta.y * basisC.x) / determinant;
            var v = (basisB.x * delta.y - basisB.y * delta.x) / determinant;
            var local = ScreenMapRefA + u * (ScreenMapRefB - ScreenMapRefA) + v * (ScreenMapRefC - ScreenMapRefA);
            var normalizedX = Mathf.InverseLerp(ScreenMapLocalMinX, ScreenMapLocalMaxX, local.x);
            var normalizedY = Mathf.InverseLerp(ScreenMapLocalMinY, ScreenMapLocalMaxY, local.y);
            return new Vector2(
                mapRect.x + Mathf.Clamp01(normalizedX) * mapRect.width,
                mapRect.y + (1f - Mathf.Clamp01(normalizedY)) * mapRect.height);
        }

        private static Vector2? RadarWorldToScreen(Vector3 worldPosition, Vector3? localPosition, Rect mapRect)
        {
            if (!localPosition.HasValue)
            {
                return null;
            }

            const float range = 220f;
            var delta = worldPosition - localPosition.Value;
            var x = Mathf.Clamp(delta.x / range, -1f, 1f);
            var y = Mathf.Clamp(delta.z / range, -1f, 1f);
            return new Vector2(mapRect.center.x + x * mapRect.width * 0.48f, mapRect.center.y - y * mapRect.height * 0.48f);
        }

        private static Vector3? FindLocalScreenMapPosition(List<ScreenMapPlayer> players)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].IsLocal)
                {
                    return players[i].Position;
                }
            }

            return players.Count > 0 ? players[0].Position : (Vector3?)null;
        }

        private static string AddScreenMapMasksFromScene(List<ScreenMapMask> masks)
        {
            return AddLockedMapPieceMasks(masks);
        }

        private static string AddLockedMapPieceMasks(List<ScreenMapMask> masks)
        {
            if (masks == null)
            {
                return "no mask list";
            }

            var board = FindBestMapBoard();
            if (board == null)
            {
                return "no board for locks";
            }

            var map = TryReadObjectMember(board, "Map") ?? TryReadObjectMember(board, "map");
            var managers = FindPlayerMapManagers();
            if (managers.Count == 0)
            {
                return "no map manager";
            }

            var bestLocked = int.MaxValue;
            var bestTotal = 0;
            var bestSource = "no lock state";
            var sawState = false;

            for (var i = 0; i < managers.Count; i++)
            {
                var manager = managers[i];
                var states = TryGetMapPieceStates(manager, map);
                if (states != null && states.Count > 0)
                {
                    sawState = true;
                    var lockedCount = CountLockedPieces(states);
                    if (IsEffectivelyFullyUnlocked(lockedCount, states.Count))
                    {
                        return "all pieces visible " + (states.Count - lockedCount).ToString() + "/" + states.Count.ToString();
                    }

                    if (lockedCount < bestLocked)
                    {
                        bestLocked = lockedCount;
                        bestTotal = states.Count;
                        bestSource = "state full cover";
                    }
                }

                var undiscovered = TryGetUndiscoveredMapPieceIndexes(manager, map);
                if (undiscovered.Count == 0)
                {
                    if (sawState)
                    {
                        continue;
                    }

                    continue;
                }

                var total = Mathf.Max(states != null ? states.Count : 0, FindMaxIndex(undiscovered) + 1, TownshipMapPieceCount);
                if (IsEffectivelyFullyUnlocked(undiscovered.Count, total))
                {
                    return "all pieces visible " + (total - undiscovered.Count).ToString() + "/" + total.ToString();
                }

                if (undiscovered.Count < bestLocked)
                {
                    bestLocked = undiscovered.Count;
                    bestTotal = total;
                    bestSource = "locked full cover";
                }
            }

            if (bestLocked != int.MaxValue)
            {
                AddFullMapMask(masks);
                return bestSource + " " + bestLocked.ToString() + "/" + bestTotal.ToString() + " from " + managers.Count.ToString() + " managers";
            }

            return "no lock state";
        }

        private static int CountLockedPieces(List<bool> states)
        {
            var locked = 0;
            if (states == null)
            {
                return locked;
            }

            for (var i = 0; i < states.Count; i++)
            {
                if (!states[i])
                {
                    locked++;
                }
            }

            return locked;
        }

        private static bool IsEffectivelyFullyUnlocked(int lockedCount, int totalCount)
        {
            if (lockedCount <= 0)
            {
                return true;
            }

            return totalCount == TownshipMapPieceCount && lockedCount <= TownshipUnusedLockedPieceTolerance;
        }

        private static void AddFullMapMask(List<ScreenMapMask> masks)
        {
            if (masks == null)
            {
                return;
            }

            masks.Add(new ScreenMapMask
            {
                NormalizedRect = new Rect(0f, 0f, 1f, 1f)
            });
        }

        private static List<object> FindPlayerMapManagers()
        {
            var managers = new List<object>();
            var mapManagerType = FindType("Alta.Map.MapManager");
            var mapManager = TryReadStaticObjectMember(mapManagerType, "Instance");
            if (mapManager == null && mapManagerType != null)
            {
                try
                {
                    var found = UnityEngine.Object.FindObjectsOfType(mapManagerType);
                    if (found != null && found.Length > 0)
                    {
                        mapManager = found[0];
                    }
                }
                catch
                {
                }
            }

            AddUniqueObject(managers, TryInvokeObjectMethod(mapManager, "GetSharedManager"));
            AddUniqueObject(managers, TryReadObjectMember(mapManager, "singleManager"));

            var managerDictionary = TryReadObjectMember(mapManager, "managers") as IDictionary;
            if (managerDictionary != null)
            {
                foreach (DictionaryEntry entry in managerDictionary)
                {
                    AddUniqueObject(managers, entry.Value);
                }
            }

            return managers;
        }

        private static List<int> TryGetUndiscoveredMapPieceIndexes(object manager, object map)
        {
            var indexes = new List<int>();
            var result = map != null
                ? TryInvokeObjectMethod(manager, "GetUndiscoveredMapPieces", map)
                : null;

            var enumerable = result as IEnumerable;
            if (enumerable == null)
            {
                return indexes;
            }

            foreach (var item in enumerable)
            {
                AddIntValue(indexes, item);
            }

            return indexes;
        }

        private static List<bool> TryGetMapPieceStates(object manager, object map)
        {
            var save = TryReadObjectMember(manager, "save");
            var states = TryReadObjectMember(save, "MapPieceStates");
            if (states == null)
            {
                states = TryReadObjectMember(save, "<MapPieceStates>k__BackingField");
            }

            var direct = ReadBooleanList(states);
            if (direct.Count > 0)
            {
                return direct;
            }

            var dictionary = states as IDictionary;
            if (dictionary != null)
            {
                if (map != null && dictionary.Contains(map))
                {
                    var mapped = ReadBooleanList(dictionary[map]);
                    if (mapped.Count > 0)
                    {
                        return mapped;
                    }
                }

                foreach (DictionaryEntry entry in dictionary)
                {
                    var mapped = ReadBooleanList(entry.Value);
                    if (mapped.Count > 0)
                    {
                        return mapped;
                    }
                }
            }

            return null;
        }

        private static int FindMapPieceStateCount(object manager, object map)
        {
            var states = TryGetMapPieceStates(manager, map);
            return states != null ? states.Count : 0;
        }

        private static List<bool> ReadBooleanList(object value)
        {
            var results = new List<bool>();
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
            {
                return results;
            }

            foreach (var item in enumerable)
            {
                if (item is bool)
                {
                    results.Add((bool)item);
                }
            }

            return results;
        }

        private static void AddIntValue(List<int> values, object value)
        {
            if (values == null || value == null)
            {
                return;
            }

            try
            {
                values.Add(Convert.ToInt32(value));
            }
            catch
            {
            }
        }

        private static int FindMaxIndex(List<int> values)
        {
            var max = -1;
            if (values == null)
            {
                return max;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        private static void AddUniqueObject(List<object> values, object value)
        {
            if (values == null || value == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (object.ReferenceEquals(values[i], value))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static string AddScreenMapPlayersFromRoster(List<ScreenMapPlayer> players)
        {
            var playerType = FindType("Player");
            var allPlayers = TryReadStaticObjectMember(playerType, "AllPlayers");
            if (AddScreenMapPlayersFromRosterValue(players, allPlayers))
            {
                return "Player.AllPlayers";
            }

            var names = new[]
            {
                "Players",
                "PlayerList",
                "OnlinePlayers",
                "ConnectedPlayers",
                "RemotePlayers",
                "CurrentPlayers",
                "ActivePlayers"
            };

            for (var i = 0; i < names.Length; i++)
            {
                var value = TryReadStaticObjectMember(playerType, names[i]);
                if (AddScreenMapPlayersFromRosterValue(players, value))
                {
                    return "Player." + names[i];
                }
            }

            return "no roster";
        }

        private static bool AddScreenMapPlayersFromRosterValue(List<ScreenMapPlayer> players, object value)
        {
            if (players == null || value == null)
            {
                return false;
            }

            var before = players.Count;
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    AddScreenMapPlayer(players, entry.Value);
                    AddScreenMapPlayer(players, entry.Key);
                }

                return players.Count > before;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    AddScreenMapPlayer(players, item);
                }

                return players.Count > before;
            }

            AddScreenMapPlayer(players, value);
            return players.Count > before;
        }

        private static void AddScreenMapPlayersFromCurrentPlayer(List<ScreenMapPlayer> players)
        {
            var playerType = FindType("Player");
            AddScreenMapPlayer(players, TryReadStaticObjectMember(playerType, "Current"));
        }

        private static void AddScreenMapPlayer(List<ScreenMapPlayer> players, object player)
        {
            if (players == null || player == null)
            {
                return;
            }

            var position = GetPlayerWorldPosition(player);
            if (!position.HasValue)
            {
                return;
            }

            var rawPositionField = TryReadObjectMember(player, "Position");
            if (rawPositionField is Vector3)
            {
                var rawPos = (Vector3)rawPositionField;
                var diff = Vector3.Distance(rawPos, position.Value);
                if (diff > 1f)
                {
                    MelonLogger.Msg($"[Minimap Debug] Position field={rawPos} vs Transform-based={position.Value} (differ by {diff:F1} units)");
                }
            }

            var sourceKey = RuntimeHelpersGetHashCode(player);
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].SourceKey == sourceKey)
                {
                    return;
                }
            }

            var isLocal = IsLocalPlayer(player);
            var name = GetPlayerDisplayName(player);
            if (!IsUsablePlayerName(name))
            {
                name = isLocal ? "Local Player" : "Player " + (players.Count + 1).ToString();
            }

            var chunk = GetPlayerChunkLabel(player, position.Value);
            players.Add(new ScreenMapPlayer
            {
                SourceKey = sourceKey,
                SourceObject = player,
                Name = name,
                Position = position.Value,
                Chunk = chunk,
                Label = name + "\n" + chunk,
                IsLocal = isLocal
            });
        }

        private static Vector3? GetPlayerWorldPosition(object player)
        {
            var transform = TryReadObjectMember(player, "PlayerTransform") as Transform;
            if (transform != null)
            {
                return transform.position;
            }

            transform = TryReadObjectMember(player, "transform") as Transform;
            if (transform != null)
            {
                return transform.position;
            }

            var position = TryReadObjectMember(player, "Position");
            if (position is Vector3)
            {
                return (Vector3)position;
            }

            return null;
        }

        private static string GetPlayerDisplayName(object player)
        {
            var name = TryReadPlayerName(player);
            if (IsUsablePlayerName(name))
            {
                return name;
            }

            var unityName = TryReadObjectMember(player, "UnityObjectName") as string;
            return IsUsablePlayerName(unityName) ? unityName : null;
        }

        private static bool IsLocalPlayer(object player)
        {
            var value = TryReadObjectMember(player, "IsLocalPlayer");
            if (value is bool && (bool)value)
            {
                return true;
            }

            var playerType = FindType("Player");
            var current = TryReadStaticObjectMember(playerType, "Current");
            return current != null && object.ReferenceEquals(current, player);
        }

        private static string GetPlayerChunkLabel(object player, Vector3 position)
        {
            var names = new[]
            {
                "Chunk",
                "CurrentChunk",
                "ChunkPosition",
                "ChunkIndex",
                "CurrentChunkIndex",
                "Region",
                "CurrentRegion"
            };

            for (var i = 0; i < names.Length; i++)
            {
                var value = TryReadObjectMember(player, names[i]);
                if (value != null)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrEmpty(text) && text != player.ToString())
                    {
                        return "chunk " + text;
                    }
                }
            }

            return "chunk " + Mathf.FloorToInt(position.x / 64f).ToString() + "," + Mathf.FloorToInt(position.z / 64f).ToString();
        }

        private static Vector2 GetMapBoardResolution(MapBoard board)
        {
            var resolution = TryReadObjectMember(board, "resolution");
            if (resolution is Vector2)
            {
                return (Vector2)resolution;
            }

            var parsedResolution = TryParseVector2Text(resolution != null ? resolution.ToString() : null);
            if (parsedResolution.HasValue)
            {
                return parsedResolution.Value;
            }

            var resolutionX = TryReadObjectMember(board, "ResolutionX");
            if (resolutionX is int && (int)resolutionX > 0)
            {
                var x = (int)resolutionX;
                return new Vector2(x, x);
            }

            if (resolutionX is float && (float)resolutionX > 0f)
            {
                var x = (float)resolutionX;
                return new Vector2(x, x);
            }

            return new Vector2(1024f, 1024f);
        }

        private static Vector2? TryParseVector2Text(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var cleaned = text.Replace("(", string.Empty).Replace(")", string.Empty);
            var parts = cleaned.Split(',');
            if (parts.Length < 2)
            {
                return null;
            }

            float x;
            float y;
            if (float.TryParse(parts[0].Trim(), out x) && float.TryParse(parts[1].Trim(), out y) && x > 0f && y > 0f)
            {
                return new Vector2(x, y);
            }

            return null;
        }

        private static MapBoard FindBestMapBoard()
        {
            try
            {
                var boards = UnityEngine.Object.FindObjectsOfType<MapBoard>();
                return boards != null && boards.Length > 0 ? boards[0] : null;
            }
            catch
            {
                return null;
            }
        }

        private static Texture FindScreenMapTexture()
        {
            var directRendererType = FindType("Alta.Map.MapBoardDirectRenderer");
            if (directRendererType == null)
            {
                return null;
            }

            UnityEngine.Object[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType(directRendererType);
            }
            catch
            {
                return null;
            }

            Texture bestTexture = null;
            var bestPixels = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var texture = TryGetRenderedMapTexture(renderers[i]);
                if (!IsUsableScreenMapTexture(texture))
                {
                    continue;
                }

                var pixels = texture.width * texture.height;
                if (pixels > bestPixels)
                {
                    bestTexture = texture;
                    bestPixels = pixels;
                }
            }

            return bestTexture;
        }

        private static bool IsUsableScreenMapTexture(Texture texture)
        {
            return texture != null && texture.width > 4 && texture.height > 4;
        }

        private static Texture2D CreateScreenMapSnapshot(Texture source)
        {
            if (!IsUsableScreenMapTexture(source))
            {
                return null;
            }

            var width = Mathf.Clamp(source.width, 64, 1024);
            var height = Mathf.Clamp(source.height, 64, 1024);
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.name = "MiniMapMod Snapshot";
                copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                copy.Apply(false, false);
                UnityEngine.Object.DontDestroyOnLoad(copy);
                return copy;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Texture TryGetRenderedMapTexture(object rendererObject)
        {
            if (rendererObject == null)
            {
                return null;
            }

            TryInvokeObjectMethod(rendererObject, "QueueRender");
            TryInvokeObjectMethod(rendererObject, "RenderMap");

            var names = new[] { "rt", "renderTexture", "renderedMap", "renderedTexture" };
            for (var i = 0; i < names.Length; i++)
            {
                var texture = TryReadObjectMember(rendererObject, names[i]) as Texture;
                if (texture != null)
                {
                    return texture;
                }
            }

            var rawMap = TryReadObjectMember(rendererObject, "map") as Texture;
            var previewRenderer = TryReadObjectMember(rendererObject, "previewRenderer") as Renderer;
            if (previewRenderer != null && previewRenderer.material != null && previewRenderer.material.mainTexture != null)
            {
                var previewTexture = previewRenderer.material.mainTexture;
                return object.ReferenceEquals(previewTexture, rawMap) ? null : previewTexture;
            }

            return null;
        }

        private static object TryInvokeObjectMethod(object instance, string name, params object[] args)
        {
            if (instance == null)
            {
                return null;
            }

            try
            {
                var methods = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method == null || method.Name != name)
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    var argumentCount = args != null ? args.Length : 0;
                    if (parameters.Length != argumentCount)
                    {
                        continue;
                    }

                    return method.Invoke(instance, args);
                }
            }
            catch
            {
            }

            return null;
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(typeName);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                for (var i = 0; i < types.Length; i++)
                {
                    var type = types[i];
                    if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static string TryReadPlayerName(object instance)
        {
            var memberNames = new[]
            {
                "Username",
                "UserName",
                "DisplayName",
                "Display_name",
                "PlayerName",
                "Nickname",
                "NickName",
                "CharacterName",
                "AccountName",
                "SteamName",
                "Name"
            };

            var direct = TryReadStringMember(instance, memberNames);
            if (IsUsablePlayerName(direct))
            {
                return direct;
            }

            var nestedMemberNames = new[]
            {
                "User",
                "Account",
                "Profile",
                "PlayerProfile",
                "PlayerInfo",
                "UserInfo",
                "Identity",
                "Owner",
                "Player"
            };

            for (var i = 0; i < nestedMemberNames.Length; i++)
            {
                var nested = TryReadObjectMember(instance, nestedMemberNames[i]);
                if (nested == null || object.ReferenceEquals(nested, instance))
                {
                    continue;
                }

                var nestedName = TryReadStringMember(nested, memberNames);
                if (IsUsablePlayerName(nestedName))
                {
                    return nestedName;
                }
            }

            return null;
        }

        private static string TryReadStringMember(object instance, string[] names)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            for (var i = 0; i < names.Length; i++)
            {
                var value = TryReadObjectMember(type, instance, names[i]);
                var text = value as string;
                if (IsUsablePlayerName(text))
                {
                    return text.Trim();
                }
            }

            return null;
        }

        private static object TryReadObjectMember(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            return TryReadObjectMember(instance.GetType(), instance, name);
        }

        private static object TryReadStaticObjectMember(Type type, string name)
        {
            if (type == null)
            {
                return null;
            }

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(null, null);
                }
            }
            catch
            {
            }

            try
            {
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    return field.GetValue(null);
                }
            }
            catch
            {
            }

            return null;
        }

        private static object TryReadObjectMember(Type type, object instance, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(instance, null);
                }
            }
            catch
            {
            }

            try
            {
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsUsablePlayerName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.Length < 2 || text.Length > 40)
            {
                return false;
            }

            return !LooksLikeGenericObjectName(text);
        }

        private static bool LooksLikeGenericObjectName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var text = value.ToLowerInvariant();
            return text.Contains("(clone)") ||
                   text == "player" ||
                   text == "playercontroller" ||
                   text == "localplayer" ||
                   text == "remoteplayer" ||
                   text.Contains("controller") ||
                   text.Contains("camera") ||
                   text.Contains("input") ||
                   text.Contains("manager") ||
                   text.Contains("network") ||
                   text.Contains("nameplate") ||
                   text.Contains("nametag") ||
                   text.Contains("minimapmod");
        }

        private static int RuntimeHelpersGetHashCode(object value)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private sealed class ScreenMapPlayer
        {
            public int SourceKey;
            public object SourceObject;
            public string Name;
            public string Chunk;
            public string Label;
            public Vector3 Position;
            public bool IsLocal;
        }

        private sealed class ScreenMapMask
        {
            public Rect NormalizedRect;
        }
    }
}
