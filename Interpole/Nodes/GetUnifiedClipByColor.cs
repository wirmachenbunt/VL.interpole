#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;

using Interpole;
using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "GetUnifiedClipByColor",
                Category = "Interpole",
                Help = "get multiple clips information as one",
                Tags = "color",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GetUnifiedClipByColor : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        
        [Input("Interpole", IsSingle = true)]
        protected Pin<InterpoleCoreNode> FInterpoleIn;

        IIOContainer<IDiffSpread<EnumEntry>> ClipColorEnum;

        [Output("Normalized")]
        protected ISpread<double> FNormalizedOut;

        [Output("Seconds")]
        protected ISpread<double> FSecondsOut;

        [Output("Fade In/Out")]
        protected ISpread<double> FFadeOut;

        [Output("Variable")]
        protected ISpread<string> FVariableOut;

        [Output("Is Active")]
        protected ISpread<bool> FIsActiveOut;

        [Output("Name", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<string> FNameOut;

        [Output("Color", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<string> FColorOut;

        [Output("Position", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<int> FPositionOut;

        [Output("Duration", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<int> FDurationOut;

        [Import()]
        public ILogger FLogger;
        [Import()]
        private IPluginHost FHost;
        [Import()]
        private IIOFactory FIOFactory;


        private int FLastFramePos;
        bool init = true;

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            FInterpoleIn.Connected += OnInterpoleConnected;
            FInterpoleIn.Disconnected += OnInterpoleDisconnected;

            //new way of enums
            InputAttribute attr = new InputAttribute("Color");
            ClipColorEnum = FIOFactory.CreateIOContainer<IDiffSpread<EnumEntry>>(attr, true);
        }

        private void OnInterpoleConnected(object sender, PinConnectionEventArgs args)
        {
            init = true;
        }

        private void OnInterpoleDisconnected(object sender, PinConnectionEventArgs args)
        {
            ResetSliceCount();
        }

        //get connected enum for states
        public void Initialize()
        {
            if (init)
            {
                var pin = ClipColorEnum.GetPluginIO() as IPin;
                (pin as IEnumIn).SetSubType(FInterpoleIn[0].myGUID + "_ClipsColor");
                init = false;
            }

        }

        public static double Clamp(double value, double min, double max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        public double CalcFade(InterpoleBase.Clip clip, int clipPos)
        {
            double value = 0.0;
            value = 1f / clip.FadeIn * clipPos;
            value = Clamp(value, 0, 1);
            if (clip.Duration - clipPos < clip.FadeOut)
            {
                value = Clamp((((double)clip.Duration - clipPos) / clip.FadeOut), 0, 1);
            }

            value = double.IsNaN(value) ? 0 : value;

            return value;
        }

        private void ResetSliceCount() {
            FNormalizedOut.SliceCount = 0;
            FSecondsOut.SliceCount = 0;
            FFadeOut.SliceCount = 0;
            FVariableOut.SliceCount = 0;
            FIsActiveOut.SliceCount = 0;
            FNameOut.SliceCount = 0;
            FColorOut.SliceCount = 0;
            FPositionOut.SliceCount = 0;
            FDurationOut.SliceCount = 0;
        }

        public void UpdateOutputs()
        {
            try
            {
                ResetSliceCount();

                int framePos = FInterpoleIn[0].PlayHead;
                int FrameRate = FInterpoleIn[0].FrameRate;

                for (int i = 0; i < ClipColorEnum.IOObject.SliceCount; i++)
                {
                    InterpoleBase.Clip clip = FInterpoleIn[0].clipList.Where(
                        c => c.Color == ClipColorEnum.IOObject[i] &&
                        c.StartFrame <= framePos &&
                        c.StartFrame + c.Duration > framePos)
                        .FirstOrDefault();

                    if (clip == null) clip = FInterpoleIn[0].clipList.FirstOrDefault(x => x.Color == ClipColorEnum.IOObject[i]);

                    if (clip != null) //fix exception
                    {
                        int clipFramePos = framePos - clip.StartFrame;
                        if (clipFramePos < 0) clipFramePos = 0;
                        if (framePos > (clip.Duration + clip.StartFrame)) clipFramePos = clip.Duration;

                        FIsActiveOut.Add(clipFramePos > 0 && clipFramePos < clip.Duration ? true : false);

                        if (FIsActiveOut[i])
                        {
                            FNormalizedOut.Add((double)clipFramePos / clip.Duration);
                            FSecondsOut.Add((double)clipFramePos / FrameRate);
                            FFadeOut.Add(CalcFade(clip, clipFramePos));
                            FPositionOut.Add(clipFramePos);
                        }
                        else // reset values to 0 if theres no clip on current time
                        {
                            FFadeOut.Add(0);
                            FNormalizedOut.Add(0);
                            FSecondsOut.Add(0);
                            FPositionOut.Add(0);
                        }

                        FVariableOut.Add(clip.Variable);
                        FNameOut.Add(clip.Name);
                        FColorOut.Add(clip.Color);
                        FDurationOut.Add(clip.Duration);

                    }              
                }
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, "updating outputs failed: " + e);
            }
        }

        public void Evaluate(int SpreadMax)
        {
            if (FInterpoleIn.IsConnected && FInterpoleIn[0].clipList != null)
            {
                Initialize();

                if (FInterpoleIn[0].clipList.Count > 0) UpdateOutputs();
            }
        }

    }
}
