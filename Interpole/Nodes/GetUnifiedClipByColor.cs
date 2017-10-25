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


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "GetUnifiedClipByColor",
                Category = "Interpole",
                Help = "get multiple clips information as one",
                Tags = "name",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GetUnifiedStateByColor : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        protected IDiffSpread<EnumEntry> EnumClip;

        [Input("Interpole")]
        public Pin<Interpole> InterpoleIn;

        [Output("Normalized")]
        public ISpread<double> Normalized;

        [Output("Seconds")]
        public ISpread<double> FSecondsOut;

        [Output("FadeInOut")]
        public ISpread<double> FadeInOut;

        [Output("Variable")]
        public ISpread<string> FVariableOut;

        [Output("IsActive")]
        public ISpread<bool> IsActive;

        [Output("ClipName", Visibility = PinVisibility.OnlyInspector)]
        public ISpread<string> FName;

        [Output("FramePosition", Visibility = PinVisibility.OnlyInspector)]
        public ISpread<int> FramePosition;

        [Output("Duration", Visibility = PinVisibility.OnlyInspector)]
        public ISpread<int> Duration;

        Interpole.Clip myclip = new Interpole.Clip();

        [Import()]
        public ILogger FLogger;
        [Import()]
        public IIOFactory FIOFactory;
        [Import()]
        IPluginHost FHost;

        string EnumName;
        bool invalidate = false;

        List<Interpole.Clip> getClipList = new List<Interpole.Clip>();

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            InterpoleIn.Connected += Input_Connected;
            InterpoleIn.Disconnected += Input_Disconnected;

            FHost.GetNodePath(true, out EnumName); //get unique node path
            EnumName += "Interpole"; // add unique name to path
            InputAttribute attr = new InputAttribute("Clip"); //name of pin
            attr.EnumName = EnumName;
            EnumClip = FIOFactory.CreateDiffSpread<EnumEntry>(attr);
        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
            //invalidate = true;
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {
            invalidate = true;
            FLogger.Log(LogType.Debug, "connected");
        }

        public void UpdateEnum()
        {
            if (invalidate || InterpoleIn[0].needsUpdate)
            {
                EnumManager.UpdateEnum(EnumName, InterpoleIn[0].clipList[0].Color, InterpoleIn[0].clipList.Select(x => x.Color).Distinct().ToArray());
                invalidate = false;
                FLogger.Log(LogType.Debug, "Update GetUnifiedClip Enum");
            }
        }

        public static double Clamp(double value, double min, double max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        public double CalcFade(Interpole.Clip myclip, int clipPos)
        {
            double value = 0.0;
            value = ((double)1 / myclip.FadeIn) * clipPos;
            value = Clamp(value, 0, 1);
            if (myclip.Duration - clipPos < myclip.FadeOut)
            {
                value = Clamp((((double)myclip.Duration - clipPos) / myclip.FadeOut), 0, 1);
            }



            return value;
        }

        public void UpdateOutputs()
        {
            try // fixes missing clip bug, when deleting a choosen clip
            {

                //set all sliceCounts to zero
                FramePosition.SliceCount = FadeInOut.SliceCount = Normalized.SliceCount = Duration.SliceCount = IsActive.SliceCount = FName.SliceCount = FSecondsOut.SliceCount = FVariableOut.SliceCount = 0;;

                for (int i = 0; i < EnumClip.SliceCount; i++)
                {

                    myclip = InterpoleIn[0].clipList.FirstOrDefault(x => x.Color == EnumClip[i]);


                    var tempCLip = InterpoleIn[0].clipList.Where(x => x.Color == EnumClip[i] &&
                                                                x.StartFrame <= InterpoleIn[0].playPosFrames &&
                                                                (x.StartFrame + x.Duration > InterpoleIn[0].playPosFrames)).FirstOrDefault();
                    if (tempCLip != null)
                    {
                        myclip = tempCLip;
                    }
                    
                    // FLogger.Log(LogType.Debug, "something went wrong");
                    
                    //myclip = InterpoleIn[0].clipList[EnumClip[i].Index]; // get enum index of selected clip

                    // get relative pointer position in frames
                    int ClipFramePos = InterpoleIn[0].playPosFrames - myclip.StartFrame;
                    if (ClipFramePos < 0) ClipFramePos = 0;
                    if (InterpoleIn[0].playPosFrames > (myclip.Duration + myclip.StartFrame)) ClipFramePos = myclip.Duration;
                    FramePosition.Add(ClipFramePos);

                    //get fadein fade out
                    double fadeInOut = CalcFade(myclip, ClipFramePos);
                    if (double.IsNaN(fadeInOut)) fadeInOut = 0;
                    FadeInOut.Add(fadeInOut);


                    //is clip active ?
                    if (fadeInOut > 0 && ClipFramePos <= (myclip.Duration - 1)) IsActive.Add(true);
                    else IsActive.Add(false);

                    //get relative pointer position normalized
                    Normalized.Add((double)1 / myclip.Duration * ClipFramePos);
                    FSecondsOut.Add((double)ClipFramePos / InterpoleIn[0].Framerate[0]);

                    //weirdo bugfix
                    if(!IsActive[i])
                    {
                        FadeInOut[i] = 0;
                        Normalized[i] = 0;
                        FSecondsOut[i] = 0;
                    }

                    // variable
                    FVariableOut.Add(myclip.Variable);

                    //duration
                    Duration.Add(myclip.Duration);

                    //Name
                    FName.Add(myclip.Name);
                }

                //FLogger.Log(LogType.Debug, "GetClip");
            }
            catch { }
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {


            if (InterpoleIn.IsConnected && InterpoleIn[0].clipList != null)
            {
                if (InterpoleIn[0].clipList.Count > 0)
                {
                    UpdateEnum();
                    UpdateOutputs();
                }
            }
        }



    }
}
