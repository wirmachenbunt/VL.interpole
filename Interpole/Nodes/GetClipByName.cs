#region usings
using System;
using System.Linq;
using System.ComponentModel.Composition;
using System.Collections.Generic;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Nodes;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{    
    #region PluginInfo
    [PluginInfo(Name = "GetClipByName",
                Category = "Interpole",
                Help = "get clip information by name",
                Tags = "name",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GetClipByName : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins

        [Input("Interpole", IsSingle = true)]
        protected Pin<Interpole> FInterpoleIn;

        // clip name
        protected IDiffSpread<EnumEntry> FNameIn;

        [Output("Normalized")]
        protected ISpread<double> FNormalizedOut;

        [Output("Seconds")]
        protected ISpread<double> FSecondsOut;

        [Output("FadeInOut")]
        protected ISpread<double> FFadeOut;

        [Output("Variable")]
        protected ISpread<string> FVariableOut;

        [Output("IsActive")]
        protected ISpread<bool> FIsActiveOut;

        [Output("Bin Size")]
        protected ISpread<int> FBinSizeOut;

        [Output("ClipName", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<string> FNameOut;

        [Output("ClipColor", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<string> FColorOut;

        [Output("FramePosition", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<int> FPositionOut;

        [Output("Duration", Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<int> FDurationOut;

        [Import()]
        public ILogger FLogger;
        [Import()]
        private IPluginHost FHost;
        [Import()]
        private IIOFactory FIOFactory;

        private List<List<Interpole.Clip>> FClipList;
        private string FEnumName;
        private bool FValid;
        private int FLastFramePos;

        #endregion fields & pins
        
        public void OnImportsSatisfied()
        {
            FInterpoleIn.Connected += OnInterpoleConnected;
            FInterpoleIn.Disconnected += OnInterpoleDisconnected;

            FHost.GetNodePath(true, out FEnumName); //get unique node path
            FEnumName += "Interpole"; // add unique name to path
            InputAttribute attr = new InputAttribute("Clip"); // name of pin
            attr.EnumName = FEnumName;
            FNameIn = FIOFactory.CreateDiffSpread<EnumEntry>(attr);
        }

        private void OnInterpoleConnected(object sender, PinConnectionEventArgs args)
        {
            FValid = false;

            // TODO: ??? find out why we can't update enum and list here
            // // if (FInterpoleIn[0].clipList != null) {
            //     UpdateEnum();
            //     UpdateClipList();
            // // }
        }

        private void OnInterpoleDisconnected(object sender, PinConnectionEventArgs args)
        {
            ResetSliceCount();
            FClipList = null;
        }

        private void UpdateEnum()
        {
            try
            {
                EnumManager.UpdateEnum(
                    FEnumName,
                    FInterpoleIn[0].clipList[0].Name,
                    FInterpoleIn[0].clipList.Select(x => x.Name).Distinct().ToArray()
                );
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, "updating enum failed: " + e);
            }
        }

        private double Clamp(double value, double min, double max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        private double CalcFade(Interpole.Clip clip, int clipPos)
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

        private void ResetSliceCount()
        {
            FNormalizedOut.SliceCount = 0;
            FSecondsOut.SliceCount = 0;
            FFadeOut.SliceCount = 0;
            FVariableOut.SliceCount = 0;
            FIsActiveOut.SliceCount = 0;
            FBinSizeOut.SliceCount = 0;
            FNameOut.SliceCount = 0;
            FColorOut.SliceCount = 0;
            FPositionOut.SliceCount = 0;
            FDurationOut.SliceCount = 0;
        }


        private void UpdateClipList()
        {
            ResetSliceCount();

            try
            {
                FClipList = null;
                FClipList = new List<List<Interpole.Clip>>();

                for (int i = 0; i < FNameIn.SliceCount; i++)
                {
                    FClipList.Add(new List<Interpole.Clip>());

                    FClipList[i].AddRange(
                        from clip in FInterpoleIn[0].clipList
                        where clip.Name == FNameIn[i]
                        orderby clip.StartFrame
                        select clip
                    );

                    FBinSizeOut.Add(FClipList[i].Count); // set bin size

                    for (int x = 0; x < FClipList[i].Count; x++) // get the static infos
                    {
                        Interpole.Clip clip = FClipList[i][x];

                        FVariableOut.Add(clip.Variable);
                        FNameOut.Add(clip.Name);
                        FColorOut.Add(clip.Color);
                        FDurationOut.Add(clip.Duration);
                    }

                    // set slice count of the updating values
                    FNormalizedOut.SliceCount = FNormalizedOut.SliceCount + FClipList[i].Count;
                    FSecondsOut.SliceCount = FSecondsOut.SliceCount + FClipList[i].Count;
                    FFadeOut.SliceCount = FFadeOut.SliceCount + FClipList[i].Count;
                    FIsActiveOut.SliceCount = FIsActiveOut.SliceCount + FClipList[i].Count;
                    FPositionOut.SliceCount = FPositionOut.SliceCount + FClipList[i].Count;
                }
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, "updating clip list failed: " + e);
            }
        }

        private void UpdateOutputs()
        {
            int framePos = FInterpoleIn[0].playPosFrames;
            int frameRate = FInterpoleIn[0].Framerate[0];

            int count = 0;

            for (int i = 0; i < FClipList.Count; i++)
            {
                for (int x = 0; x < FClipList[i].Count; x++)
                {
                    Interpole.Clip clip = FClipList[i][x];
                    int clipFramePos = framePos - clip.StartFrame;
                    if (clipFramePos < 0 ) clipFramePos = 0;
                    if (framePos > (clip.Duration + clip.StartFrame)) clipFramePos = clip.Duration;

                    // TODO: try to find a way to iterate only on the relevant ones
                    FNormalizedOut[x + count] = (double)clipFramePos / clip.Duration;
                    FSecondsOut[x + count] = (double)clipFramePos / frameRate;
                    FFadeOut[x + count] = CalcFade(clip, clipFramePos);
                    FIsActiveOut[x + count] = clipFramePos > 0 && clipFramePos < clip.Duration ? true : false;
                    FPositionOut[x + count] = clipFramePos;
                }
                count += FClipList[i].Count;
            }
        }

        public void Evaluate(int SpreadMax)
        {
            if (FInterpoleIn.IsConnected && FInterpoleIn[0].clipList != null)
            {
                 // update everything
                if (FInterpoleIn[0].needsUpdate || FNameIn.IsChanged || !FValid)
                {
                    UpdateEnum();
                    UpdateClipList();
                    UpdateOutputs();
                    FValid = true;
                }

                // needs to be updated every frame to have instant feedback while editing clips
                // TODO?: another possibilty would be firing needsUpdate while clips are getting edited
                if (FInterpoleIn[0].clipList.Count > 0) UpdateOutputs();
            }
        }
        
    }
}