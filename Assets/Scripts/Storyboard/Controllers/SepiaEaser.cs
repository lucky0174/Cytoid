namespace Cytoid.Storyboard.Controllers
{
    public class SepiaEaser : StoryboardRendererEaser<ControllerState>
    {
        public SepiaEaser(StoryboardRenderer renderer) : base(renderer)
        {
        }

        public override void OnUpdate()
        {
            if (Config.UseEffects)
            {
                if (From.Sepia.IsSet())
                {
                    Provider.Sepia.enabled = From.Sepia.Value;
                    if (From.Sepia.Value && From.SepiaIntensity.IsSet())
                    {
                        Provider.Sepia._Fade = EaseFloat(From.SepiaIntensity, To.SepiaIntensity);
                    }
                }
            }
        }
    }
}