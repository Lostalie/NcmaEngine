namespace Ncma.Animation;

// Closed recipe shape validation, including every unused row. Resource numerics are checked by each owned consumer.
public static class AnimationPoseRecipe
{
    public static void Validate(ReadOnlySpan<AnimationPoseInstruction> plan,int output,ulong? frozenGeneration=null)
    {
        if(plan.IsEmpty||plan.Length>AnimationGraphCodec.MaxPlanInstructions||output<0||output>=plan.Length)throw new ArgumentException("Bounded pose recipe/output required.");
        for(int i=0;i<plan.Length;i++){
            var r=plan[i];if(r.NodeId==Guid.Empty)throw new ArgumentException("Pose node identity required.");
            bool Back(int at)=>at>=0&&at<i;
            if(r.Operation==AnimationPoseOperation.Clip){if(r.CacheGeneration!=0||r.SourceA!=-1||r.SourceB!=-1||r.SourceC!=-1||r.ClipId==Guid.Empty||r.Weight!=0||!double.IsFinite(r.Duration)||r.Duration is <.001 or >600||!double.IsFinite(r.Previous)||!double.IsFinite(r.Current)||r.Previous<0||r.Current<r.Previous||!r.Loop&&r.Current>r.Duration)throw new ArgumentException("Exact Clip recipe interval required.");continue;}
            if(r.ClipId!=Guid.Empty||r.Previous!=0||r.Current!=0||r.Duration!=0||r.Loop||!float.IsFinite(r.Weight)||r.Weight is <0 or >1)throw new ArgumentException("Neutral bounded composite fields required.");
            switch(r.Operation){
                case AnimationPoseOperation.Blend:if(r.CacheGeneration!=0||r.SourceC!=-1||!Back(r.SourceA)||!Back(r.SourceB))throw new ArgumentException("Backward blend.");break;
                case AnimationPoseOperation.RootSource:if(r.CacheGeneration!=0||r.SourceC!=-1||r.Weight!=0||!Back(r.SourceA)||!Back(r.SourceB)||plan[r.SourceB].Operation!=AnimationPoseOperation.Clip)throw new ArgumentException("Exact primary root row.");break;
                case AnimationPoseOperation.LayerOverride:if(r.CacheGeneration!=0||r.SourceC!=-1||!Back(r.SourceA)||!Back(r.SourceB))throw new ArgumentException("Backward override.");break;
                case AnimationPoseOperation.LayerAdditive:if(r.CacheGeneration!=0||!Back(r.SourceC)||!Back(r.SourceA)||!Back(r.SourceB)||plan[r.SourceC].Operation!=AnimationPoseOperation.Clip||plan[r.SourceC].Previous!=plan[r.SourceC].Current||plan[r.SourceC].Loop)throw new ArgumentException("Exact additive reference.");break;
                case AnimationPoseOperation.Frozen:if(r.SourceA!=-1||r.SourceB!=-1||r.SourceC!=-1||r.Weight!=0||r.CacheGeneration==0||r.CacheGeneration>9007199254740991UL||frozenGeneration.HasValue&&r.CacheGeneration!=frozenGeneration)throw new ArgumentException("Exact frozen generation.");break;
                default:throw new ArgumentException("Closed pose operation.");
            }
        }
    }
}
