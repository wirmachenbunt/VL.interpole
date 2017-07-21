#region usings
using System;
using System.ComponentModel.Composition;

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
    [PluginInfo(Name = "GetClip",
                Category = "Clip Animation",
                Help = "get a clip information",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GetState : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        protected IDiffSpread<EnumEntry> EnumClip;

        [Input("Interpole")]
        public Pin<Interpole> InterpoleIn;

        [Output("FramePosition")]
        public ISpread<int> FramePosition;

        [Output("FadeInOut")]
        public ISpread<double> FadeInOut;

        [Output("IsActive")]
        public ISpread<bool> IsActive;

        Interpole.Clip myclip = new Interpole.Clip();

        [Import()]
        public ILogger FLogger;
        [Import()]
        public IIOFactory FIOFactory;
        [Import()]
        IPluginHost FHost;

        string EnumName;

        //private bool invalidate = true;

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            InterpoleIn.Connected += Input_Connected;
            InterpoleIn.Disconnected += Input_Disconnected;

            FHost.GetNodePath(true, out EnumName); //get unique node path
            EnumName += "Interpole"; // add unique name to path
            InputAttribute attr = new InputAttribute("Clip"); //name of pin
            attr.EnumName = EnumName;
            //attr.DefaultEnumEntry = "Init"; //default state
            EnumClip = FIOFactory.CreateDiffSpread<EnumEntry>(attr);
        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
            //invalidate = true;
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {
            //invalidate = true;
            FLogger.Log(LogType.Debug, "connected");
        }

        public void UpdateEnum()
        {
            if (InterpoleIn[0].clipList.Count > 0 && InterpoleIn[0].needsUpdate)
            {
                EnumManager.UpdateEnum(EnumName, InterpoleIn[0].clipList[0].Name, InterpoleIn[0].clipList.Select(x => x.Name).ToArray());
            }
            InterpoleIn[0].needsUpdate = false;
        }

        public void UpdateOutputs()
        {
            
            myclip = InterpoleIn[0].clipList[EnumClip[0].Index]; // get enum index of selected clip

            int ClipFramePos = InterpoleIn[0].playPosFrames - myclip.StartFrame;
            if (ClipFramePos < 0) ClipFramePos = 0;
            if (InterpoleIn[0].playPosFrames > (myclip.Duration + myclip.StartFrame)) ClipFramePos = myclip.Duration;
            FramePosition[0] = ClipFramePos;
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            UpdateEnum();

            if (InterpoleIn.IsConnected)
            {
                UpdateEnum();
                UpdateOutputs();

            }
        }



    }
}
