using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Platform;
using Ncma.Application;

namespace Ncma.Editor.App;

// Keep visibility guards at the caller: unopened bodies are not compiled on the common Build path.
internal sealed unsafe partial class EditorPresenter
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildSceneFileControls(EditorPage _page, float w, float toolbar, bool header, bool writable, ref ulong labelId)
    {
        if(workspaceStyle){Panel(1,"场景文件",8,54,Math.Min(600,w-16),240);var overlay=_items[^1];overlay.Value=2;_items[^1]=overlay;}
        else Panel(1, "Scene / 场景", 0, header?64:0, w, toolbar-(header?64:0));
        if(!workspaceStyle)Add(GuiItemKind.Theme,12,1,"",number:preferences?.Current.Theme switch {"Light"=>1,"Classic"=>2,_=>0});
        Add(GuiItemKind.Label, 3, labelId++, $"{workspace.Owner.Document.Name} | Revision {_page.State.Revision} | {(_page.State.Dirty ? "Modified" : "Saved")} | {_page.State.FilePath ?? "Unsaved"}");
        Add(GuiItemKind.Text, 2, 1, "Scene path (.ncmascene)", new("path"), _path);
        Add(GuiItemKind.Checkbox, 2, 2, "Confirm replacing unsaved document", new("discard"), number: _discardConfirmed ? 1 : 0, max: 1, enabled: writable);
        Button(3, "New", "new", writable); Line(); Button(4, "Open", "open", writable && _path.Length > 0); Line(); Button(5, "Save", "save", writable);
        Button(6, "Undo", "undo", writable && _page.State.UndoCount > 0); Line(); Button(7, "Redo", "redo", writable && _page.State.RedoCount > 0);
        if(filePicker is not null) {
            Line(); Button(40,"Open Scene...","browse_open",writable);Line(); Button(41,"Save Scene As...","browse_save",writable);
        }
        if(preferences is not null) { Line(); Button(42,_showPreferences?"Hide Preferences":"Preferences","preferences_toggle"); }
        if(_characters is not null){Line();Add(GuiItemKind.Button,22,1,_showCharacters?"Hide Character Debug":"Character Debug",new("character_toggle"));}
        if(_runtimeInspection is not null){Line();Add(GuiItemKind.Button,62,1,"Runtime inspection",new("runtime_toggle"));}
        if(workspace.Owner.RuntimeChanges is not null){Line();Add(GuiItemKind.Button,64,1,"审阅并保留 Play 改动",new("keep_play_toggle"));}
        End();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildSelectedInspector(SceneObjectData selected, Ncma.Scripting.ScriptCatalogSnapshot catalog, bool writable, ref ulong labelId)
    {
            Add(GuiItemKind.Label, 3, labelId++, selected.Id.ToString());
            if (Encoding.UTF8.GetByteCount(selected.Name) <= 1023)
                Add(GuiItemKind.Text, 2, 11, "Name", new("name", selected.Id), selected.Name, enabled: writable);
            else Add(GuiItemKind.Label, 3, labelId++, selected.Name + " (exceeds single-widget text budget)");
            ulong control = 100;
            _componentOffset = Math.Clamp(_componentOffset,0,Math.Max(0,(selected.Components.Length-1)/8*8));
            Button(30,"Previous components","components_previous",_componentOffset>0);
            Line();
            Button(31,"Next components","components_next",_componentOffset+8<selected.Components.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Components {selected.Components.Length}; page {_componentOffset/8+1}");
            foreach (var component in selected.Components.Skip(_componentOffset).Take(8))
            {
                Add(GuiItemKind.Label, 3, labelId++, component.TypeId);
                if (component.TypeId == "ncma.transform")
                {
                    var transform = component.Data.Deserialize<TransformData>(DataJson);
                    double[] values = [transform.Position.X, transform.Position.Y, transform.Position.Z, transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W, transform.Scale.X, transform.Scale.Y, transform.Scale.Z];
                    string[] names = ["Position X", "Position Y", "Position Z", "Rotation X", "Rotation Y", "Rotation Z", "Rotation W", "Scale X", "Scale Y", "Scale Z"];
                    for (int i = 0; i < values.Length; i++) Add(GuiItemKind.Number, 2, control++, names[i], new("transform", selected.Id, Index: i), number: values[i], min: i is >= 3 and <= 6 ? -1 : -100, max: i is >= 3 and <= 6 ? 1 : 100, enabled: writable);
                }
                else if (Encoding.UTF8.GetByteCount(component.Data.GetRawText()) <= 1023 && !(_jsonEdit is { } jsonActive && jsonActive.Object == selected.Id && jsonActive.Type == component.TypeId))
                    Add(GuiItemKind.Text, 2, control++, "Component JSON", new("component", selected.Id, component.TypeId, component.Version), component.Data.GetRawText(), enabled: writable);
                else
                {
                    var key = (selected.Id, component.TypeId);
                    // During activation use the immutable baseline: changing page length must not
                    // shift page boundaries while the native text cursor/IME draft is active.
                    JsonTextPages pages;
                    if (_jsonEdit is { } active && active.Object == selected.Id && active.Type == component.TypeId) pages = active.Pages;
                    else if (!_jsonPageCache.TryGetValue(key, out pages!)) {
                        pages = new JsonTextPages(component.Data.GetRawText());
                        if (_jsonPageCache.Count >= 32) _jsonPageCache.Clear();
                        _jsonPageCache.Add(key, pages);
                    }
                    int page = Math.Clamp(_jsonPageOffsets.GetValueOrDefault(key), 0, pages.Count - 1);
                    _jsonPageOffsets[key] = page;
                    Add(GuiItemKind.Button, 2, control++, "Previous JSON page", new("json_previous", selected.Id, component.TypeId), enabled: page > 0);
                    Line();
                    Add(GuiItemKind.Button, 2, control++, "Next JSON page", new("json_next", selected.Id, component.TypeId), enabled: page + 1 < pages.Count);
                    Add(GuiItemKind.Label, 3, labelId++, $"Component JSON page {page + 1}/{pages.Count}; full JSON validates on commit");
                    Add(GuiItemKind.Text, 2, control++, "Component JSON page",
                        new("component_page", selected.Id, component.TypeId, component.Version, new JsonPageEdit(selected.Id, component.TypeId, page, pages)),
                        pages.Text(page), enabled: writable);
                }
                if (component.TypeId == RenderConfiguration.ComponentType)
                {
                    var config = component.Data.Deserialize<RenderConfiguration>(DataJson);
                    (string Field, string Label, double Value, double Min, double Max)[] fields = [
                        ("exposure", "Reference exposure", config.Exposure, .01, 16),
                        ("metallic", "Reference metallic", config.Metallic, 0, 1),
                        ("roughness", "Reference roughness", config.Roughness, .04, 1),
                        ("toneExposureOverride", "Tone exposure override (0 = default)", config.ToneExposureOverride, 0, 16),
                        ("featureExposure", "Feature exposure (0 = off)", config.FeatureExposure, 0, 16),
                        ("clearRed", "Clear red", config.ClearRed, 0, 1),
                        ("clearGreen", "Clear green", config.ClearGreen, 0, 1),
                        ("clearBlue", "Clear blue", config.ClearBlue, 0, 1),
                        ("baseRed","Base color red",config.BaseRed,0,1),("baseGreen","Base color green",config.BaseGreen,0,1),("baseBlue","Base color blue",config.BaseBlue,0,1),
                        ("lightIntensity","Light intensity",config.LightIntensity,0,20),("ambient","Ambient",config.Ambient,0,.25),
                        ("constantBias","Shadow constant bias",config.ConstantBias,0,.01),("slopeBias","Shadow slope bias",config.SlopeBias,0,8),
                        ("shadowDistance","Shadow distance",config.ShadowDistance,5,100),("cascadeLambda","Cascade lambda",config.CascadeLambda,0,1),
                        ("shadowFilter","Shadow filter: 0 PCF3 / 1 PCF5 / 2 PCSS",config.ShadowFilter,0,2),("lightRadius","Light angular radius",config.LightRadius,.001,.2),
                        ("contactSteps","Contact steps",config.ContactSteps,4,32),("contactDistance","Contact distance",config.ContactDistance,.05,3),
                        ("contactThickness","Contact thickness",config.ContactThickness,.005,.3),("contactStrength","Contact strength",config.ContactStrength,0,1)
                    ];
                    foreach (var field in fields)
                        Add(GuiItemKind.Number, 2, control++, field.Label, new("render_field", selected.Id, field.Field),
                            number: field.Value, min: field.Min, max: field.Max, enabled: writable);
                    Add(GuiItemKind.Checkbox, 2, control++, "Replace tone stage", new("render_field", selected.Id, "replaceToneStage"),
                        number: config.ReplaceToneStage ? 1 : 0, max: 1, enabled: writable);
                    Add(GuiItemKind.Checkbox,2,control++,"Directional shadows",new("render_field",selected.Id,"shadowEnabled"),number:config.ShadowEnabled?1:0,max:1,enabled:writable);
                    Add(GuiItemKind.Checkbox,2,control++,"Contact shadows",new("render_field",selected.Id,"contactEnabled"),number:config.ContactEnabled?1:0,max:1,enabled:writable);
                    Add(GuiItemKind.Label, 3, labelId++, "Reference PBR/soft shadow controls; 4 cascades × 2048. Not a material asset authoring system.");
                }
                if (component.TypeId == Ncma.Scene.Rendering.EnvironmentLightingData.TypeId)
                    BuildEnvironmentInspector(selected.Id, component.Data.Deserialize<Ncma.Scene.Rendering.EnvironmentLightingData>(DataJson), ref control, ref labelId, writable);
                if(component.TypeId==Ncma.Scene.Rendering.PostProcessData.TypeId)
                    BuildPostInspector(selected.Id,component.Data.Deserialize<Ncma.Scene.Rendering.PostProcessData>(DataJson),ref control,ref labelId,writable);
                if(component.TypeId==Ncma.Scene.Rendering.SceneShadowData.TypeId)
                    BuildShadowInspector(selected.Id,component.Data.Deserialize<Ncma.Scene.Rendering.SceneShadowData>(DataJson),ref control,ref labelId,writable);
                Button(control++, "Remove " + component.TypeId, "transaction", writable, selected.Id, new { op = "remove_component", objectId = selected.Id, typeId = component.TypeId });
            }
            if (!selected.Components.Any(c => c.TypeId == "ncma.transform"))
                Button(control++, "Add Transform", "transaction", writable, selected.Id, new { op = "set_component", objectId = selected.Id, typeId = "ncma.transform", version = 1, data = workspace.Owner.Document.Components.Encode(TransformData.Identity) });
            if (workspace.Owner.Document.Components.Describe().Any(c => c.TypeId == Ncma.Scene.Rendering.EnvironmentLightingData.TypeId) &&
                !workspace.HasSceneComponent(Ncma.Scene.Rendering.EnvironmentLightingData.TypeId))
                Button(control++, "添加环境配置（关闭）", "transaction", writable, selected.Id, new { op = "set_component", objectId = selected.Id,
                    typeId = Ncma.Scene.Rendering.EnvironmentLightingData.TypeId, version = 1, data = workspace.Owner.Document.Components.Encode(Ncma.Scene.Rendering.EnvironmentLightingData.Off) });
            BuildEnvironmentAuthoring(selected.Id,ref control,ref labelId,writable);
            BuildPostCatalog(selected.Id,ref control,ref labelId,writable);
            BuildShadowSelection(selected.Id,ref control,writable);
            _bindingOffset = Math.Clamp(_bindingOffset,0,Math.Max(0,(selected.Behaviours.Length-1)/8*8));
            Button(32,"Previous bindings","bindings_previous",_bindingOffset>0);
            Line();
            Button(33,"Next bindings","bindings_next",_bindingOffset+8<selected.Behaviours.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Bindings {selected.Behaviours.Length}; page {_bindingOffset/8+1}");
            foreach (var binding in selected.Behaviours.Skip(_bindingOffset).Take(8))
            {
                Add(GuiItemKind.Label, 3, labelId++, binding.TypeName);
                Add(GuiItemKind.Checkbox, 2, control++, "Enabled", new("binding", selected.Id, binding.Id.ToString()), number: binding.Enabled ? 1 : 0, max: 1, enabled: writable);
                int exportOffset = Math.Clamp(_exportOffsets.GetValueOrDefault(binding.Id),0,Math.Max(0,(binding.Exports.Length-1)/16*16));
                _exportOffsets[binding.Id] = exportOffset;
                Add(GuiItemKind.Button,2,control++,"Previous Exports",new("exports_page",binding.Id,Index:-16),enabled:exportOffset>0);
                Line();
                Add(GuiItemKind.Button,2,control++,"Next Exports",new("exports_page",binding.Id,Index:16),enabled:exportOffset+16<binding.Exports.Length);
                Add(GuiItemKind.Label,3,labelId++,$"Exports {binding.Exports.Length}; page {exportOffset/16+1}");
                foreach (var export in binding.Exports.Skip(exportOffset).Take(16))
                    Add(export.Kind == ExportKind.Boolean ? GuiItemKind.Checkbox : GuiItemKind.Number, 2, control++, export.Name,
                        new("export", selected.Id, binding.Id + "/" + export.Name, (int)export.Kind), number: export.Value,
                        min: export.Kind == ExportKind.Boolean ? 0 : -1000, max: export.Kind == ExportKind.Boolean ? 1 : 1000, enabled: writable);
                Button(control++, "Remove binding", "transaction", writable, selected.Id, new { op = "remove_binding", objectId = selected.Id, bindingId = binding.Id });
            }
            _catalogOffset = Math.Clamp(_catalogOffset,0,Math.Max(0,(catalog.Types.Length-1)/8*8));
            Button(34,"Previous script types","catalog_previous",_catalogOffset>0);
            Line();
            Button(35,"Next script types","catalog_next",_catalogOffset+8<catalog.Types.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Trusted types {catalog.Types.Length}; page {_catalogOffset/8+1}");
            foreach (var type in catalog.Types.Skip(_catalogOffset).Take(8))
                Button(control++, "Attach " + type.TypeName, "attach", writable, selected.Id, type);
            Add(GuiItemKind.Text, 2, 12, "Type UUID to confirm deletion", new("delete_text"), _deleteConfirmation, enabled: writable);
            Button(13, "Delete confirmed object", "delete", writable && Guid.TryParse(_deleteConfirmation, out Guid confirmed) && confirmed == selected.Id, selected.Id);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildLocalPreferences(EditorPreferencesStore preferences, EditorPreferences prefs, ref ulong labelId)
    {
            Add(GuiItemKind.Label,3,labelId++,"Local preferences only; separate reversible history. Font path/size apply after restart; no docking.");
            Add(GuiItemKind.Number,11,1,"Theme: 0 Dark / 1 Light / 2 Classic",new("preference_theme"),number:prefs.Theme switch {"Light"=>1,"Classic"=>2,_=>0},max:2);
            Add(GuiItemKind.Number,11,2,"Sidebar width",new("preference_side"),number:prefs.SideWidth,min:180,max:480);
            Add(GuiItemKind.Number,11,3,"Toolbar height",new("preference_toolbar"),number:prefs.ToolbarHeight,min:160,max:300);
            Add(GuiItemKind.Checkbox,11,4,"Show Console",new("preference_console"),number:prefs.ShowConsole?1:0,max:1);
            Add(GuiItemKind.Checkbox,11,5,"Show isolated previews",new("preference_previews"),number:prefs.ShowPreviews?1:0,max:1);
            Add(GuiItemKind.Text,11,6,"Font path (restart)",new("preference_font"),value:prefs.FontPath);
            Add(GuiItemKind.Number,11,7,"Font size (restart)",new("preference_font_size"),number:prefs.FontSize,min:8,max:64);
            Add(GuiItemKind.Button,11,8,"Save local preferences",new("preferences_save",Operation:preferences.Revision));
            Add(GuiItemKind.Button,11,9,"Preferences Undo",new("preferences_undo",Operation:preferences.Revision),enabled:preferences.CanUndo);
            Add(GuiItemKind.Button,11,10,"Preferences Redo",new("preferences_redo",Operation:preferences.Revision),enabled:preferences.CanRedo);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildFbxPreview(FbxPreviewSession fbxPreview, float center, ref ulong labelId)
    {
            _fbxStatus = fbxPreview.Capture();
            Add(GuiItemKind.Label, 3, labelId++, "FBX — isolated C# preview/history; not a scene Animator");
            Add(GuiItemKind.Text, 7, 1, "Absolute FBX path", new("fbx_path"), _fbxPath);
            Add(GuiItemKind.Button, 7, 2, "Import FBX", new("fbx_import", Operation: _fbxStatus.Revision), enabled: _fbxPath.Length > 0);
            if(filePicker is not null)Add(GuiItemKind.Button,7,13,"Open FBX...",new("fbx_browse",Operation:_fbxStatus.Revision));
            Line();
            Add(GuiItemKind.Button, 7, 3, "Preview Undo", new("fbx_undo", Operation: _fbxStatus.Revision), enabled: _fbxStatus.UndoCount > 0);
            Line();
            Add(GuiItemKind.Button, 7, 4, "Preview Redo", new("fbx_redo", Operation: _fbxStatus.Revision), enabled: _fbxStatus.RedoCount > 0);
            if (_fbxStatus.AssetId is not null)
            {
                Add(GuiItemKind.Label, 3, labelId++, $"Asset {_fbxStatus.AssetId} | {_fbxStatus.Bones} bones | {_fbxStatus.Meshes} meshes");
                Add(GuiItemKind.Label, 3, labelId++, $"Clip {Short(_fbxStatus.ClipName!)} | {_fbxStatus.Time:F3}/{_fbxStatus.Duration:F3}s");
                Add(GuiItemKind.Button, 7, 5, _fbxStatus.Paused ? "Play FBX" : "Pause FBX",
                    new(_fbxStatus.Paused ? "fbx_resume" : "fbx_pause", Operation: _fbxStatus.Revision));
                Line();
                Add(GuiItemKind.Button, 7, 6, "FBX Step 1/60", new("fbx_step", Operation: _fbxStatus.Revision));
                Line();
                Add(GuiItemKind.Button, 7, 7, "Inspect CPU pose", new("fbx_sample", Operation: _fbxStatus.Revision));
                _fbxClipOffset = Math.Clamp(_fbxClipOffset, 0, Math.Max(0, (fbxPreview.ClipCount - 1) / 8 * 8));
                Add(GuiItemKind.Button, 7, 8, "Previous clips", new("fbx_previous"), enabled: _fbxClipOffset > 0);
                Line();
                Add(GuiItemKind.Button, 7, 9, "Next clips", new("fbx_next"), enabled: _fbxClipOffset + 8 < fbxPreview.ClipCount);
                for (int i = _fbxClipOffset; i < Math.Min(fbxPreview.ClipCount, _fbxClipOffset + 8); i++)
                    Add(GuiItemKind.Button, 7, 100 + (ulong)i, Short(fbxPreview.ClipName(i)),
                        new("fbx_clip", Index: i, Operation: _fbxStatus.Revision));
                if (_fbxSample.Length != 0 && _sampleRevision == _fbxStatus.Revision && _sampleTime == _fbxStatus.Time)
                {
                    Add(GuiItemKind.Label, 3, labelId++, $"CPU sample: {_fbxSample.Length} copied floats (matrices + skinned vertices)");
                    for (int i = 0; i < Math.Min(8, _fbxStatus.Bones); i++)
                        Add(GuiItemKind.Label, 3, labelId++, $"Bone {i}: ({_fbxSample[i*16+12]:F3}, {_fbxSample[i*16+13]:F3}, {_fbxSample[i*16+14]:F3})");
                }
                Add(GuiItemKind.Number,7,10,"FBX Orbit",new("fbx_orbit"),number:_fbxYaw,min:-Math.PI,max:Math.PI);
                Canvas(8,_wireframe.Build(fbxPreview,_fbxYaw,Math.Max(1,center-32),340));
                Add(GuiItemKind.Label,3,labelId++,$"CPU wireframe: {_wireframe.DisplayedTriangles}/{_wireframe.TotalTriangles} triangles; cap=10000; reference cube is separate.");
                if (_reportResource != fbxPreview.ResourceIdentity) {
                    _fbxReport = InspectionText.Split(fbxPreview.Report!.Value.GetRawText());
                    _reportResource = fbxPreview.ResourceIdentity; _reportOffset = 0;
                }
                Add(GuiItemKind.Button,7,11,"Previous report page",new("fbx_report_previous"),enabled:_reportOffset>0);
                Line(); Add(GuiItemKind.Button,7,12,"Next report page",new("fbx_report_next"),enabled:_reportOffset+4<_fbxReport.Length);
                Add(GuiItemKind.Label,3,labelId++,$"Full import report: chunks {_reportOffset+1}..{Math.Min(_reportOffset+4,_fbxReport.Length)} / {_fbxReport.Length} (includes skeleton names/parents and warnings)");
                foreach (string chunk in _fbxReport.Skip(_reportOffset).Take(4)) Add(GuiItemKind.Label,3,labelId++,chunk);
            }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildActionPreview(ActionAnimationSession animation, float center, ref ulong labelId)
    {
                using var json = JsonDocument.Parse(animation.InspectJson()); var state = json.RootElement;
                var revision = animation.Revision;
                Add(GuiItemKind.Label,3,labelId++,"Isolated C# action preview/history; native ABI 2 samples only; no scene Animator.");
                Add(GuiItemKind.Number,9,2,"Locomotion speed",new("animation_command",Index:1,Operation:revision),number:state.GetProperty("speed").GetDouble(),max:1);
                foreach (var (id,label,command,value,text,enabled) in new (ulong,string,int,double,string,bool)[] {
                    (3,animation.Paused?"Play Preview":"Pause Preview",3,animation.Paused?0:1,"",true),
                    (4,"Animation Step 1/60",4,1.0/60,"",true),(5,"Attack",2,0,"Attack",true),(6,"Dodge",2,0,"Dodge",true),
                    (7,"Reset Animation",5,0,"",true),(8,"Animation Undo",6,0,"",state.GetProperty("can_undo").GetBoolean()),
                    (9,"Animation Redo",7,0,"",state.GetProperty("can_redo").GetBoolean()) })
                    Add(GuiItemKind.Button,9,id,label,new("animation_button",Field:text,Index:command,Operation:(revision,value)),enabled:enabled);
                Add(GuiItemKind.Label,3,labelId++,$"State {state.GetProperty("state").GetString()} time={animation.Time:F3} blend={state.GetProperty("blend_weight").GetDouble():F3}");
                var lines = new List<PreviewLine>(); var bones=state.GetProperty("bones");
                System.Numerics.Vector2 Point(int i) { var p=bones[i].GetProperty("position");return new(.28f+(p[0].GetSingle()+p[2].GetSingle()*.45f)*120/Math.Max(1,center-32),248f/340-(p[1].GetSingle()-p[2].GetSingle()*.15f)*120/340); }
                for(int i=0;i<bones.GetArrayLength();i++) {
                    int parent=bones[i].GetProperty("parent").GetInt32();var point=Point(i);
                    if(parent>=0)lines.Add(new(Point(parent),point,0xFFFF879Au,3));lines.Add(new(point,point,0xFFF5E1DCu,4));
                }
                Canvas(10,CollectionsMarshal.AsSpan(lines));
                foreach (string chunk in InspectionText.Split(state.GetRawText())) Add(GuiItemKind.Label,3,labelId++,chunk);
    }
}
