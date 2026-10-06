using Ncma.Gui;
using Ncma.Editor.Core;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private CharacterInspectionService? _characters;
    private bool _showCharacters,_characterReviewed;
    private readonly HashSet<Guid> _characterSelection=[];
    private CharacterReadReview? _characterReview;
    private Guid _characterSession;
    internal void AttachCharacters(CharacterInspectionService service)=>_characters=service;
    private void BuildCharacterDebug(float width,float height,ref ulong labelId)
    {
        if(!_showCharacters||_characters is null)return;
        Panel(6,"Character / Combat Debug (read-only)",width*.15f,height*.15f,width*.65f,height*.7f);
        var frame=_characters.LocalFrame();
        if(frame is null){Add(GuiItemKind.Label,3,labelId++,"Start a physics-bound Play session to inspect copied diagnostics.");_characterSelection.Clear();_characterReview=null;_characterReviewed=false;End();return;}
        if(_characterSession!=frame.PlaySessionId){_characterSession=frame.PlaySessionId;_characterSelection.Clear();_characterReview=null;_characterReviewed=false;}
        if(_characterReview is {} old&&!_characters.IsCurrent(old)){_characterReview=null;_characterReviewed=false;}
        Add(GuiItemKind.Label,3,labelId++,$"Play {frame.PlaySessionId:D} / World {frame.WorldId:D} / Tick {frame.Tick} / {frame.State}");
        Add(GuiItemKind.Label,3,labelId++,$"Committed snapshot={frame.SnapshotValid} / fault={frame.FaultCode} / last-step events={frame.Events.Length}");
        ulong item=100;
        foreach(var row in frame.Characters) {
            Add(GuiItemKind.Label,3,labelId++,$"{row.ObjectId:D} | {row.Action} #{row.Instance} t={row.Time:F3} | ground={row.Ground} contacts={row.Contacts} health={row.Health}");
            Add(GuiItemKind.Label,3,labelId++,$"Position {row.Position}; desired {row.Desired}; accepted {row.Accepted}; root={row.RootEnabled}");
            Add(GuiItemKind.Checkbox,22,item++,"Select exact read-only character UUID",new("character_select",row.ObjectId),number:_characterSelection.Contains(row.ObjectId)?1:0,max:1);
        }
        foreach(var e in frame.Events.Take(16))Add(GuiItemKind.Label,3,labelId++,$"Tick {e.Tick}: {e.Kind} / actor {e.Actor:D} / instance {e.Instance}");
        var grant=_characters.Grant;
        Add(GuiItemKind.Label,3,labelId++,$"MCP approved {grant.Objects.Length}/32, {grant.Seconds}s remaining. Shared paired-audience scope, NOT per-client ACL.");
        Add(GuiItemKind.Button,22,2,"Review exact Play/object scope and paired audience",new("character_review"),enabled:frame.SnapshotValid&&_characterSelection.Count>0&&workspace.Owner.Endpoint is {} ep&&ep.View.Connections.Any(c=>c.Paired&&c.Connected));
        Add(GuiItemKind.Button,22,5,"Revoke all character read grants",new("character_revoke"));
        if(_characterReview is {} review) {
            Add(GuiItemKind.Label,3,labelId++,$"Read-only Play {review.PlaySessionId:D}; World {review.WorldId:D}; endpoint {review.EndpointId:D}");
            foreach(Guid id in review.ObjectIds)Add(GuiItemKind.Label,3,labelId++,"Read ONLY object "+id.ToString("D"));
            foreach(Guid id in review.Audience)Add(GuiItemKind.Label,3,labelId++,"Paired audience "+id.ToString("D"));
            Add(GuiItemKind.Checkbox,22,3,"I reviewed every UUID and the complete paired audience",new("character_reviewed"),number:_characterReviewed?1:0,max:1);
            Add(GuiItemKind.Button,22,4,"Approve displayed read-only scope for 60 seconds",new("character_approve",Field:review.Fingerprint),enabled:_characterReviewed);
        }
        End();
    }
    private bool ApplyCharacterAction(ActionView action,double value)
    {
        if(!action.Kind.StartsWith("character_",StringComparison.Ordinal))return false;
        if(action.Kind=="character_toggle"){_showCharacters=!_showCharacters;return true;}
        if(_characters is null)throw new EditRejectedException("character_service_missing");
        switch(action.Kind) {
            case "character_select":
                if(value is not(0 or 1))throw new EditRejectedException("character_selection_invalid");
                if(value==0)_characterSelection.Remove(action.Object);
                else{if(_characterSelection.Count>=32&&!_characterSelection.Contains(action.Object))throw new EditRejectedException("character_selection_budget");_characterSelection.Add(action.Object);}
                _characterReview=null;_characterReviewed=false;break;
            case "character_review":_characterReview=_characters.Capture(_characterSelection.ToArray());_characterReviewed=false;break;
            case "character_reviewed":_characterReviewed=value==1;break;
            case "character_approve":
                if(_characterReview is not {} review||!_characterSelection.SetEquals(review.ObjectIds))throw new EditRejectedException("character_review_stale");
                _characters.Approve(review,action.Field,_characterReviewed);_characterReview=null;_characterReviewed=false;break;
            case "character_revoke":_characters.Revoke();_characterReview=null;_characterReviewed=false;break;
            default:throw new EditRejectedException("character_intent_invalid");
        }
        return true;
    }
}
