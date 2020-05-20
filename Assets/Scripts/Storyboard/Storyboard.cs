using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UniRx.Async;
using UnityEngine;

namespace Cytoid.Storyboard
{
    public class Storyboard
    {
        public Game Game { get; }
        public StoryboardRenderer Renderer { get; }
        public StoryboardConfig Config { get; }
        
        public readonly JObject RootObject;
        
        public readonly List<Text> Texts = new List<Text>();
        public readonly List<Sprite> Sprites = new List<Sprite>();
        public readonly List<Controller> Controllers = new List<Controller>();
        public readonly List<NoteController> NoteControllers = new List<NoteController>();
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<Trigger> Triggers = new List<Trigger>();
        public readonly Dictionary<string, JObject> Templates = new Dictionary<string, JObject>();

        public Storyboard(Game game, string content)
        {
            Game = game;
            Renderer = new StoryboardRenderer(this);
            Config = new StoryboardConfig(this);
            
            // Parse storyboard file

            RootObject = JObject.Parse(content);
            /*JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };*/ // Moved to Context.cs

            // Templates
            if (RootObject["templates"] != null)
            {
                foreach (var templateProperty in RootObject["templates"].Children<JProperty>())
                {
                    Templates[templateProperty.Name] = templateProperty.Value.ToObject<JObject>();
                }
            }

            // Text
            if (RootObject["texts"] != null)
            {
                foreach (var childToken in (JArray) RootObject["texts"])
                {
                    foreach (var objectToken in PopulateJObjects((JObject) childToken))
                    {
                        var text = LoadObject<Text, TextState>(objectToken);
                        if (text != null) Texts.Add(text);
                    }
                }
            }

            // Sprite
            if (RootObject["sprites"] != null)
            {
                foreach (var childToken in (JArray) RootObject["sprites"])
                {
                    foreach (var objectToken in PopulateJObjects((JObject) childToken))
                    {
                        var sprite = LoadObject<Sprite, SpriteState>(objectToken);
                        if (sprite != null) Sprites.Add(sprite);
                    }
                }
            }

            // Controller
            if (RootObject["controllers"] != null)
            {
                foreach (var childToken in (JArray) RootObject["controllers"])
                {
                    foreach (var objectToken in PopulateJObjects((JObject) childToken))
                    {
                        // Controllers must have a time
                        if (objectToken["time"] == null) objectToken["time"] = 0;

                        var controller = LoadObject<Controller, ControllerState>(objectToken);
                        if (controller != null) Controllers.Add(controller);
                    }
                }
            }

            // Note controllers
            if (RootObject["note_controllers"] != null)
            {
                foreach (var childToken in (JArray) RootObject["note_controllers"])
                {
                    foreach (var objectToken in PopulateJObjects((JObject) childToken))
                    {
                        var noteController = LoadObject<NoteController, NoteControllerState>(objectToken);
                        if (noteController != null) NoteControllers.Add(noteController);
                    }
                }
            }
                
            // Lines
            if (RootObject["lines"] != null)
            {
                foreach (var childToken in (JArray) RootObject["lines"])
                {
                    foreach (var objectToken in PopulateJObjects((JObject) childToken))
                    {
                        var line = LoadObject<Line, LineState>(objectToken);
                        if (line != null) Lines.Add(line);
                    }
                }
            }

            // Trigger
            if (RootObject["triggers"] != null)
                foreach (var objectToken in (JArray) RootObject["triggers"])
                    Triggers.Add(LoadTrigger(objectToken));
            // Register note clear listener for triggers
            Game.onNoteClear.AddListener(OnNoteClear);
            Game.onGameDisposed.AddListener(_ => Dispose());
        }

        public void Dispose()
        {
            Texts.Clear();
            Sprites.Clear();
            Controllers.Clear();
            NoteControllers.Clear();
            Triggers.Clear();
            Templates.Clear();
        }

        public async UniTask Initialize()
        {
            await Renderer.Initialize();
            Game.onGameUpdate.AddListener(Renderer.OnGameUpdate);
        }

        public void OnNoteClear(Game game, Note note)
        {
            foreach (var trigger in Triggers)
            {
                if (trigger.Type == TriggerType.NoteClear && trigger.Notes.Contains(note.Model.id))
                {
                    trigger.Triggerer = note;
                    OnTrigger(trigger);
                }

                if (trigger.Type == TriggerType.Combo && Game.State.Combo == trigger.Combo)
                {
                    trigger.Triggerer = note;
                    OnTrigger(trigger);
                }

                if (trigger.Type == TriggerType.Score && Game.State.Score >= trigger.Score)
                {
                    trigger.Triggerer = note;
                    OnTrigger(trigger);
                    Triggers.Remove(trigger);
                }
            }
        }

        public void OnTrigger(Trigger trigger)
        {
            Renderer.OnTrigger(trigger);
            
            // Destroy trigger if needed
            trigger.CurrentUses++;
            if (trigger.CurrentUses == trigger.Uses)
            {
                Triggers.Remove(trigger);
            }
        }

        public JObject Compile()
        {
            RecursivelyParseTime(RootObject);
            return RootObject;
        }

        private void RecursivelyParseTime(JObject obj)
        {
            foreach (var x in obj)
            {
                var name = x.Key;
                var value = x.Value;
                if (name == "time")
                {
                    value.Replace(ParseTime(obj, value));
                }
                else
                {
                    switch (value)
                    {
                        case JArray array:
                            RecursivelyParseTime(array);
                            break;
                        case JObject jObject:
                            RecursivelyParseTime(jObject);
                            break;
                    }
                }
            }
        }

        private void RecursivelyParseTime(JArray array)
        {
            foreach (var x in array)
            {
                switch (x)
                {
                    case JArray jArray:
                        RecursivelyParseTime(jArray);
                        break;
                    case JObject jObject:
                        RecursivelyParseTime(jObject);
                        break;
                }
            }
        }

        /**
         * Convert an object with an array of `time` to multiple objects.
         */
        private List<JObject> PopulateJObjects(JObject obj)
        {
            var timePopulatedObjects = new List<JObject>();

            var timeToken = obj.SelectToken("relative_time");
            if (timeToken != null && timeToken.Type == JTokenType.Array)
                foreach (var time in timeToken.Values())
                {
                    var newObj = (JObject) obj.DeepClone();
                    newObj["relative_time"] = time;
                    timePopulatedObjects.Add(newObj);
                }

            timeToken = obj.SelectToken("add_time");
            if (timeToken != null && timeToken.Type == JTokenType.Array)
                foreach (var time in timeToken.Values())
                {
                    var newObj = (JObject) obj.DeepClone();
                    newObj["add_time"] = time;
                    timePopulatedObjects.Add(newObj);
                }

            timeToken = obj.SelectToken("time");
            if (timeToken != null && timeToken.Type == JTokenType.Array)
                foreach (var time in timeToken.Values())
                {
                    var newObj = (JObject) obj.DeepClone();
                    newObj["time"] = time;
                    timePopulatedObjects.Add(newObj);
                }

            timePopulatedObjects = timePopulatedObjects.Count == 0 ? new List<JObject> {obj} : timePopulatedObjects;

            var populatedObjects = new List<JObject>();

            foreach (var obj2 in timePopulatedObjects)
            {
                var noteSelectorToken = obj.SelectToken("note");
                if (noteSelectorToken != null && noteSelectorToken.Type == JTokenType.Object)
                {
                    // Note selector
                    var noteSelector = new NoteSelector();
                    noteSelector.Start = (int?) noteSelectorToken.SelectToken("start") ?? noteSelector.Start;
                    noteSelector.End = (int?) noteSelectorToken.SelectToken("end") ?? noteSelector.End;
                    var typeToken = noteSelectorToken.SelectToken("type");
                    if (typeToken != null)
                    {
                        if (typeToken.Type == JTokenType.Integer)
                        {
                            noteSelector.Types.Add((int) typeToken);
                        }
                        else if (typeToken.Type == JTokenType.Array)
                        {
                            foreach (var noteToken in typeToken.Values())
                            {
                                noteSelector.Types.Add((int) noteToken);
                            }
                        }
                    }
                    else
                    {
                        ((NoteType[]) Enum.GetValues(typeof(NoteType))).ForEach(it => noteSelector.Types.Add((int) it));
                    }

                    var noteIds = new List<int>();
                    foreach (var chartNote in Game.Chart.Model.note_list)
                    {
                        if (noteSelector.Types.Contains(chartNote.type)
                            && noteSelector.Start <= chartNote.id
                            && noteSelector.End >= chartNote.id)
                        {
                            noteIds.Add(chartNote.id);
                        }
                    }

                    foreach (var noteId in noteIds)
                    {
                        var newObj = (JObject) obj2.DeepClone();
                        newObj["note"] = noteId;
                        populatedObjects.Add(newObj);
                    }
                }
                else
                {
                    populatedObjects.AddRange(timePopulatedObjects);
                }
            }

            return populatedObjects;
        }

        private Trigger LoadTrigger(JToken token)
        {
            var json = token.ToObject<JObject>();
            var trigger = new Trigger();

            trigger.Type = json["type"] != null
                ? (TriggerType) Enum.Parse(typeof(TriggerType), (string) json["type"], true)
                : TriggerType.None;
            trigger.Uses = (int?) json.SelectToken("uses") ?? trigger.Uses;

            trigger.Notes = json["notes"] != null ? json.SelectToken("notes").Values<int>().ToList() : trigger.Notes;
            trigger.Spawn = json["spawn"] != null ? json.SelectToken("spawn").Values<string>().ToList() : trigger.Spawn;
            trigger.Destroy = json["destroy"] != null
                ? json.SelectToken("destroy").Values<string>().ToList()
                : trigger.Destroy;
            trigger.Combo = (int?) json.SelectToken("combo") ?? trigger.Combo;
            trigger.Score = (int?) json.SelectToken("score") ?? trigger.Score;

            return trigger;
        }

        private TO LoadObject<TO, TS>(JToken token) where TO : Object<TS>, new() where TS : ObjectState
        {
            var states = new List<TS>();
            var obj = token.ToObject<JObject>();

            // Create initial state
            var initialState = (TS) CreateState(typeof(TS), (TS) null, obj);
            states.Add(initialState);

            // Create template states
            if (obj.TryGetValue("template", out var tmp))
            {
                var templateId = (string) tmp;
                var templateObject = Templates[templateId];

                // Template has states?
                if (templateObject["states"] != null)
                    AddStates(states, initialState, templateObject, ParseTime(obj, obj.SelectToken("time")));
            }

            // Create inline states
            AddStates(states, initialState, obj, ParseTime(obj, obj.SelectToken("time")));

            return new TO
            {
                Id = (string) obj["id"] ?? Path.GetRandomFileName(),
                States = states.OrderBy(state => state.Time).ToList() // Must sort by time
            };
        }

        private void AddStates<T>(List<T> states, T baseState, JObject rootObject, float? rootBaseTime)
            where T : ObjectState
        {
            var baseTime = ParseTime(rootObject, rootObject.SelectToken("time")) ?? rootBaseTime ?? float.MaxValue; // We set this to float.MaxValue, so if time is not set, the object is not displayed

            if (rootObject["states"] != null && rootObject["states"].Type != JTokenType.Null)
            {
                var lastTime = baseTime;

                var allStates = new JArray();
                foreach (var childToken in (JArray) rootObject["states"])
                {
                    var populatedChildren = PopulateJObjects((JObject) childToken);
                    foreach (var child in populatedChildren) allStates.Add(child);
                }

                foreach (var stateJson in allStates)
                {
                    var stateObject = stateJson.ToObject<JObject>();
                    var objectState = CreateState(typeof(T), baseState, stateObject);

                    if (objectState.Time != float.MaxValue) baseTime = objectState.Time;

                    var relativeTime = (float?) stateObject["relative_time"];

                    if (relativeTime != null)
                    {
                        objectState.RelativeTime = (float) relativeTime;
                        // Use base time + relative time
                        objectState.Time = baseTime + (float) relativeTime;
                    }

                    var addTime = (float?) stateObject["add_time"];

                    if (addTime != null)
                    {
                        objectState.AddTime = (float) addTime;
                        // Use last time + add time
                        objectState.Time = lastTime + (float) addTime;
                    }

                    states.Add((T) objectState);
                    baseState = (T) objectState;

                    lastTime = objectState.Time;

                    // Add inline states
                    if (stateObject["states"] != null) AddStates(states, baseState, stateObject, rootBaseTime);
                }
            }
        }

        private ObjectState CreateState<T>(Type type, T baseState, JObject stateObject) where T : ObjectState
        {
            if ((bool?) stateObject["reset"] == true) baseState = null; // Allow resetting states

            // Load template
            JObject templateObject = null;
            if (stateObject["template"] != null)
            {
                var templateId = (string) stateObject["template"];
                templateObject = Templates[templateId];

                if (templateObject != null)
                {
                    // Put relative time and add time
                    if (stateObject["relative_time"] == null)
                        stateObject["relative_time"] = templateObject["relative_time"];

                    if (stateObject["add_time"] == null) stateObject["add_time"] = templateObject["add_time"];

                    // Put template states
                    if (stateObject["states"] == null) stateObject["states"] = templateObject["states"];
                }
            }

            if (type == typeof(TextState))
            {
                var state = baseState != null ? (baseState as TextState).JsonDeepCopy() : new TextState();
                if (templateObject != null) ParseTextState(state, templateObject, baseState as TextState);
                ParseTextState(state, stateObject, baseState as TextState);
                return state;
            }

            if (type == typeof(SpriteState))
            {
                var state = baseState != null ? (baseState as SpriteState).JsonDeepCopy() : new SpriteState();
                if (templateObject != null) ParseSpriteState(state, templateObject, baseState as SpriteState);
                ParseSpriteState(state, stateObject, baseState as SpriteState);
                return state;
            }

            if (type == typeof(ControllerState))
            {
                var state = baseState != null ? (baseState as ControllerState).JsonDeepCopy() : new ControllerState();
                if (templateObject != null) ParseControllerState(state, templateObject, baseState as ControllerState);
                ParseControllerState(state, stateObject, baseState as ControllerState);
                return state;
            }
            
            if (type == typeof(NoteControllerState))
            {
                var state = baseState != null ? (baseState as NoteControllerState).JsonDeepCopy() : new NoteControllerState();
                if (templateObject != null) ParseNoteControllerState(state, templateObject, baseState as NoteControllerState);
                ParseNoteControllerState(state, stateObject, baseState as NoteControllerState);
                return state;
            }
            
            if (type == typeof(LineState))
            {
                var state = baseState != null ? (baseState as LineState).JsonDeepCopy() : new LineState();
                if (templateObject != null) ParseLineState(state, templateObject, baseState as LineState);
                ParseLineState(state, stateObject, baseState as LineState);
                return state;
            }

            throw new ArgumentException();
        }

        private Dictionary<string, object> replacements = new Dictionary<string, object>();

        private float? ParseTime(JObject obj, JToken token)
        {
            if (token == null) return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) return (float) token;

            if (token.Type == JTokenType.String)
            {
                var split = ((string) token).Split(':');
                var type = split[0].ToLower();
                var offset = 0f;
                if (split.Length == 3) offset = float.Parse(split[2]);

                var id = split[1].Let(it =>
                {
                    if (it == "$note")
                    {
                        var noteToken = obj.SelectToken("note");
                        if (noteToken != null)
                        {
                            var value = (int) noteToken;
                            replacements["note"] = value;
                            return value;
                        }
                        if (!replacements.ContainsKey("note"))
                        {
                            throw new Exception("$note not found");
                        }
                        return (int) replacements["note"];
                    }
                    return int.Parse(it);
                });
                var note = Game.Chart.Model.note_map[id];
                switch (type)
                {
                    case "intro":
                        return note.intro_time + offset;
                    case "start":
                        return note.start_time + offset;
                    case "end":
                        return note.end_time + offset;
                    case "at":
                        return note.start_time + (note.end_time - note.start_time) * offset;
                }
            }

            return null;
        }

        private enum ReferenceUnit
        {
            World,
            StageX, StageY, // Canvas: 800 x 600 
            NoteX, NoteY, // Notes: 1 x 1
            CameraX, CameraY, // Orthographic
        }

        private float? ParseNumber(JToken token, ReferenceUnit defaultReferenceUnit, bool scaleToCanvas)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.Integer) return ConvertNumber((int) token, defaultReferenceUnit);
            if (token.Type == JTokenType.Float) return ConvertNumber((float) token, defaultReferenceUnit);
            if (token.Type == JTokenType.String)
            {
                var split = ((string) token).Split(':');
                if (split.Length == 1) return ConvertNumber(float.Parse(split[0]), defaultReferenceUnit);
                var type = split[0].ToLower();
                var value = float.Parse(split[1]);
                return ConvertNumber(value, (ReferenceUnit) Enum.Parse(typeof(ReferenceUnit), type, true));
            }

            float ConvertNumber(float value, ReferenceUnit referenceSpace)
            {
                float res;
                switch (referenceSpace)
                {
                    case ReferenceUnit.World:
                        res = value;
                        break;
                    case ReferenceUnit.StageX:
                        res = value / StoryboardRenderer.ReferenceWidth * Game.camera.orthographicSize / UnityEngine.Screen.height * UnityEngine.Screen.width;
                        break;
                    case ReferenceUnit.StageY:
                        res = value / StoryboardRenderer.ReferenceHeight * Game.camera.orthographicSize;
                        break;
                    case ReferenceUnit.NoteX:
                        res = Game.Chart.ConvertChartXToScreenX(value);
                        break;
                    case ReferenceUnit.NoteY:
                        res = Game.Chart.ConvertChartYToScreenY(value);
                        break;
                    case ReferenceUnit.CameraX:
                        res = value * Game.camera.orthographicSize / UnityEngine.Screen.height * UnityEngine.Screen.width;
                        break;
                    case ReferenceUnit.CameraY:
                        res = value * Game.camera.orthographicSize;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                if (scaleToCanvas)
                {
                    switch (referenceSpace)
                    {
                        case ReferenceUnit.StageX:
                        case ReferenceUnit.NoteX:
                        case ReferenceUnit.CameraX:
                            res = res / (Game.camera.orthographicSize / UnityEngine.Screen.height * UnityEngine.Screen.width) * Renderer.Provider.CanvasRect.width;
                            break;
                        case ReferenceUnit.StageY:
                        case ReferenceUnit.NoteY:
                        case ReferenceUnit.CameraY:
                            res = res / Game.camera.orthographicSize * Renderer.Provider.CanvasRect.height;
                            break;
                    }
                }
                return res;
            }
            throw new ArgumentException();
        }

        private void ParseObjectState(ObjectState state, JObject json, ObjectState baseState)
        {
            var token = json.SelectToken("time");
            state.Time = ParseTime(json, token) ?? state.Time;

            state.Easing = json["easing"] != null
                ? (EasingFunction.Ease) Enum.Parse(typeof(EasingFunction.Ease), (string) json["easing"], true)
                : EasingFunction.Ease.Linear;
            state.Destroy = (bool?) json.SelectToken("destroy") ?? state.Destroy;
        }

        private void ParseCanvasObjectState(CanvasObjectState state, JObject json, CanvasObjectState baseState)
        {
            ParseObjectState(state, json, baseState);

            state.X = ParseNumber(json.SelectToken("x"), ReferenceUnit.StageX, true) ?? state.X;
            state.Y = ParseNumber(json.SelectToken("y"), ReferenceUnit.StageY, true) ?? state.Y;

            if (baseState != null)
            {
                var baseX = baseState.X;
                if (!baseX.IsSet()) baseX = 0;
                var baseY = baseState.Y;
                if (!baseY.IsSet()) baseY = 0;

                var dx = ParseNumber(json.SelectToken("dx"), ReferenceUnit.StageX, true);
                var dy = ParseNumber(json.SelectToken("dy"), ReferenceUnit.StageY, true);

                if (dx != null) state.X = baseX + (float) dx;
                if (dy != null) state.Y = baseY + (float) dy;
            }

            state.RotX = (float?) json.SelectToken("rot_x") ?? state.RotX;
            state.RotY = (float?) json.SelectToken("rot_y") ?? state.RotY;
            state.RotZ = (float?) json.SelectToken("rot_z") ?? state.RotZ;

            state.ScaleX = (float?) json.SelectToken("scale_x") ?? state.ScaleX;
            state.ScaleY = (float?) json.SelectToken("scale_y") ?? state.ScaleY;
            if (json["scale"] != null)
            {
                var scale = (float) json.SelectToken("scale");
                state.ScaleX = scale;
                state.ScaleY = scale;
            }

            state.Opacity = (float?) json.SelectToken("opacity") ?? state.Opacity;

            state.Width = ParseNumber(json.SelectToken("width"), ReferenceUnit.StageX, true) ?? state.Width;
            state.Height = ParseNumber(json.SelectToken("height"), ReferenceUnit.StageY, true) ?? state.Height;
            state.FillWidth = (bool?) json.SelectToken("fill_width") ?? state.FillWidth;

            state.Layer = (int?) json.SelectToken("layer") ?? state.Layer;
            state.Order = (int?) json.SelectToken("order") ?? state.Order;
        }

        private void ParseTextState(TextState state, JObject json, TextState baseState)
        {
            ParseCanvasObjectState(state, json, baseState);

            state.Font = (string) json.SelectToken("font") ?? state.Font;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color"), out var tmp))
                state.Color = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            state.Text = (string) json.SelectToken("text") ?? state.Text;
            state.Size = (int?) json.SelectToken("size") ?? state.Size;
            state.Align = (string) json.SelectToken("align") ?? state.Align;
        }

        private void ParseSpriteState(SpriteState state, JObject json, SpriteState baseState)
        {
            ParseCanvasObjectState(state, json, baseState);

            state.Path = (string) json.SelectToken("path") ?? state.Path;
            state.PreserveAspect = (bool?) json.SelectToken("preserve_aspect") ?? state.PreserveAspect;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color"), out var tmp))
                state.Color = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
        }
        
        private void ParseNoteControllerState(NoteControllerState state, JObject json, NoteControllerState baseState)
        {
            ParseObjectState(state, json, baseState);
            
            state.Note = (int?) json.SelectToken("note") ?? state.Note;
            state.OverrideX = (bool?) json.SelectToken("override_x") ?? state.OverrideX;
            state.X = ParseNumber(json.SelectToken("x"), ReferenceUnit.NoteX, false) ?? state.X;
            state.OverrideY = (bool?) json.SelectToken("override_y") ?? state.OverrideY;
            state.Y = ParseNumber(json.SelectToken("y"), ReferenceUnit.NoteY, false) ?? state.Y;
            state.Rot = (float?) json.SelectToken("rot") ?? state.Rot;
            state.OverrideRingColor = (bool?) json.SelectToken("override_color") ?? state.OverrideRingColor;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color"), out var tmp))
                state.RingColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            state.OverrideFillColor = (bool?) json.SelectToken("override_color") ?? state.OverrideFillColor;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color"), out var tmp2))
                state.FillColor = new Color {R = tmp2.r, G = tmp2.g, B = tmp2.b, A = tmp2.a};
            state.OpacityMultiplier = (float?) json.SelectToken("opacity_multiplier") ?? state.OpacityMultiplier;
            state.SizeMultiplier = (float?) json.SelectToken("size_multiplier") ?? state.SizeMultiplier;
            state.HoldDirection = (int?) json.SelectToken("hold_direction") ?? state.HoldDirection;
            state.Style = (int?) json.SelectToken("style") ?? state.Style;
        }
        
        private void ParseLineState(LineState state, JObject json, LineState baseState)
        {
            ParseObjectState(state, json, baseState);

            json.SelectToken("pos").ToArray().ForEach(it =>
            {
                var pos = new LinePosition();;
                pos.X = ParseNumber(it.SelectToken("x"), ReferenceUnit.NoteX, false) ?? 0;
                pos.Y = ParseNumber(it.SelectToken("y"), ReferenceUnit.NoteY, false) ?? 0;
                state.Pos.Add(pos);
            });
            state.Width = ParseNumber(json.SelectToken("width"), ReferenceUnit.World, false) ?? state.Width;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color"), out var tmp))
                state.Color = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            state.Opacity = (float?) json.SelectToken("opacity") ?? state.Opacity;
            state.Layer = (int?) json.SelectToken("layer") ?? state.Layer;
            state.Order = (int?) json.SelectToken("order") ?? state.Order;
        }

        private void ParseControllerState(ControllerState state, JObject json, ControllerState baseState)
        {
            ParseObjectState(state, json, baseState);

            state.StoryboardOpacity = (float?) json.SelectToken("storyboard_opacity") ?? state.StoryboardOpacity;
            state.UiOpacity = (float?) json.SelectToken("ui_opacity") ?? state.UiOpacity;
            state.ScanlineOpacity = (float?) json.SelectToken("scanline_opacity") ?? state.ScanlineOpacity;
            state.BackgroundDim = (float?) json.SelectToken("background_dim") ?? state.BackgroundDim;

            state.Size = (float?) json.SelectToken("size") ?? state.Size;
            state.Fov = (float?) json.SelectToken("fov") ?? state.Fov;
            state.Perspective = (bool?) json.SelectToken("perspective") ?? state.Perspective;
            state.X = ParseNumber(json.SelectToken("x"), ReferenceUnit.CameraX, false) ?? state.X;
            state.Y = ParseNumber(json.SelectToken("y"), ReferenceUnit.CameraY, false) ?? state.Y;
            state.RotX = (float?) json.SelectToken("rot_x") ?? state.RotX;
            state.RotY = (float?) json.SelectToken("rot_y") ?? state.RotY;
            state.RotZ = (float?) json.SelectToken("rot_z") ?? state.RotZ;

            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("scanline_color"), out var tmp))
                state.ScanlineColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            state.ScanlineSmoothing = (bool?) json.SelectToken("scanline_smoothing") ?? state.ScanlineSmoothing;
            state.OverrideScanlinePos = (bool?) json.SelectToken("override_scanline_pos") ?? state.OverrideScanlinePos;
            state.ScanlinePos = ParseNumber(json.SelectToken("scanline_pos"), ReferenceUnit.NoteY, false) ?? state.ScanlinePos;
            state.NoteOpacityMultiplier =
                (float?) json.SelectToken("note_opacity_multiplier") ?? state.NoteOpacityMultiplier;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("note_ring_color"), out tmp))
                state.NoteRingColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            var fillColors = json.SelectToken("note_fill_colors") != null &&
                             json.SelectToken("note_fill_colors").Type != JTokenType.Null
                ? json.SelectToken("note_fill_colors").Values<string>().ToList()
                : new List<string>();
            if (fillColors.Count > 0)
            {
                state.NoteFillColors = new List<Color>();
                for (var i = 0; i < 10; i++)
                {
                    if (i >= fillColors.Count)
                    {
                        state.NoteFillColors.Add(null);
                        continue;
                    }

                    var fillColor = fillColors[i];
                    if (ColorUtility.TryParseHtmlString(fillColor, out tmp))
                        state.NoteFillColors.Add(new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a});
                }
            }

            state.Bloom = (bool?) json.SelectToken("bloom") ?? state.Bloom;
            state.BloomIntensity = (float?) json.SelectToken("bloom_intensity") ?? state.BloomIntensity;

            state.Vignette = (bool?) json.SelectToken("vignette") ?? state.Vignette;
            state.VignetteIntensity = (float?) json.SelectToken("vignette_intensity") ?? state.VignetteIntensity;

            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("vignette_color"), out tmp))
                state.VignetteColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};
            state.VignetteStart = (float?) json.SelectToken("vignette_start") ?? state.VignetteStart;
            state.VignetteEnd = (float?) json.SelectToken("vignette_end") ?? state.VignetteEnd;

            state.Chromatic = (bool?) json.SelectToken("chromatic") ?? state.Chromatic;
            state.ChromaticIntensity = (float?) json.SelectToken("chromatic_intensity") ?? state.ChromaticIntensity;
            state.ChromaticStart = (float?) json.SelectToken("chromatic_start") ?? state.ChromaticStart;
            state.ChromaticEnd = (float?) json.SelectToken("chromatic_end") ?? state.ChromaticEnd;

            state.RadialBlur = (bool?) json.SelectToken("radial_blur") ?? state.RadialBlur;
            state.RadialBlurIntensity = (float?) json.SelectToken("radial_blur_intensity") ?? state.RadialBlurIntensity;

            state.ColorAdjustment = (bool?) json.SelectToken("color_adjustment") ?? state.ColorAdjustment;
            state.Brightness = (float?) json.SelectToken("brightness") ?? state.Brightness;
            state.Saturation = (float?) json.SelectToken("saturation") ?? state.Saturation;
            state.Contrast = (float?) json.SelectToken("contrast") ?? state.Contrast;

            state.ColorFilter = (bool?) json.SelectToken("color_filter") ?? state.ColorFilter;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("color_filter_color"), out tmp))
                state.ColorFilterColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};

            state.GrayScale = (bool?) json.SelectToken("gray_scale") ?? state.GrayScale;
            state.GrayScaleIntensity = (float?) json.SelectToken("gray_scale_intensity") ?? state.GrayScaleIntensity;

            state.Noise = (bool?) json.SelectToken("noise") ?? state.Noise;
            state.NoiseIntensity = (float?) json.SelectToken("noise_intensity") ?? state.NoiseIntensity;

            state.Sepia = (bool?) json.SelectToken("sepia") ?? state.Sepia;
            state.SepiaIntensity = (float?) json.SelectToken("sepia_intensity") ?? state.SepiaIntensity;

            state.Dream = (bool?) json.SelectToken("dream") ?? state.Dream;
            state.DreamIntensity = (float?) json.SelectToken("dream_intensity") ?? state.DreamIntensity;

            state.Fisheye = (bool?) json.SelectToken("fisheye") ?? state.Fisheye;
            state.FisheyeIntensity = (float?) json.SelectToken("fisheye_intensity") ?? state.FisheyeIntensity;

            state.Shockwave = (bool?) json.SelectToken("shockwave") ?? state.Shockwave;
            state.ShockwaveSpeed = (float?) json.SelectToken("shockwave_speed") ?? state.ShockwaveSpeed;

            state.Focus = (bool?) json.SelectToken("focus") ?? state.Focus;
            state.FocusIntensity = (float?) json.SelectToken("focus_intensity") ?? state.FocusIntensity;
            state.FocusSize = (float?) json.SelectToken("focus_size") ?? state.FocusSize;
            state.FocusSpeed = (float?) json.SelectToken("focus_speed") ?? state.FocusSpeed;
            if (ColorUtility.TryParseHtmlString((string) json.SelectToken("focus_color"), out tmp))
                state.FocusColor = new Color {R = tmp.r, G = tmp.g, B = tmp.b, A = tmp.a};

            state.Glitch = (bool?) json.SelectToken("glitch") ?? state.Glitch;
            state.GlitchIntensity = (float?) json.SelectToken("glitch_intensity") ?? state.GlitchIntensity;

            state.Artifact = (bool?) json.SelectToken("artifact") ?? state.Artifact;
            state.ArtifactIntensity = (float?) json.SelectToken("artifact_intensity") ?? state.ArtifactIntensity;
            state.ArtifactColorisation =
                (float?) json.SelectToken("artifact_colorisation") ?? state.ArtifactColorisation;
            state.ArtifactParasite = (float?) json.SelectToken("artifact_parasite") ?? state.ArtifactParasite;
            state.ArtifactNoise = (float?) json.SelectToken("artifact_noise") ?? state.ArtifactNoise;

            state.Arcade = (bool?) json.SelectToken("arcade") ?? state.Arcade;
            state.ArcadeIntensity = (float?) json.SelectToken("arcade_intensity") ?? state.ArcadeIntensity;
            state.ArcadeInterferanceSize =
                (float?) json.SelectToken("arcade_interference_size") ?? state.ArcadeInterferanceSize;
            state.ArcadeInterferanceSpeed =
                (float?) json.SelectToken("arcade_interference_speed") ?? state.ArcadeInterferanceSpeed;
            state.ArcadeContrast = (float?) json.SelectToken("arcade_contrast") ?? state.ArcadeContrast;

            state.Chromatical = (bool?) json.SelectToken("chromatical") ?? state.Chromatical;
            state.ChromaticalFade = (float?) json.SelectToken("chromatical_fade") ?? state.ChromaticalFade;
            state.ChromaticalIntensity =
                (float?) json.SelectToken("chromatical_intensity") ?? state.ChromaticalIntensity;
            state.ChromaticalSpeed = (float?) json.SelectToken("chromatical_speed") ?? state.ChromaticalSpeed;

            state.Tape = (bool?) json.SelectToken("tape") ?? state.Tape;
        }
    }
}