#region usings
using System;
using System.ComponentModel.Composition;

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
    [PluginInfo(Name = "Interpole Core",
                Category = "Interpole",
                Help = "",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class InterpoleCoreNode : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins    

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Input("Set Timecode")]
        public IDiffSpread<bool> FSetTimecode;

        [Input("Frame Clock", IsSingle = true)]
        public IDiffSpread<int> FExternalClock;

        [Input("Set Frame Clock")]
        public IDiffSpread<bool> FSetExternalClock;

        [Input("InterpoleIn")]
        public Pin<InterpoleUI> FInterpoleIn;

        [Output("ClipXML")]
        public ISpread<string> ClipXMLOut;

        int PlayHead = 0;

        [Import()]
        public ILogger FLogger;

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            FInterpoleIn.Connected += Input_Connected;
            FInterpoleIn.Disconnected += Input_Disconnected;


        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {

            FLogger.Log(LogType.Debug, "connected");
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (FInterpoleIn.IsConnected && FInterpoleIn[0] != null) // only do this when there is a connection to the UI
            {
                if (FInterpoleIn[0].interpoleForm.play) // UI is playing, get core playhead to UI
                {
                    PlayHead++;
                    FInterpoleIn[0].interpoleForm.ipD.PlayHead = PlayHead;
                }
                else PlayHead = FInterpoleIn[0].interpoleForm.ipD.PlayHead; //if UI is not playing, get UI playhead to core

                
            }

            //FLogger.Log(LogType.Debug, "nope"); 


        }

    }
}

