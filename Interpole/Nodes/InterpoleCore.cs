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
           
           
                try
                {
                //FLogger.Log(LogType.Debug, "hallo");
                FInterpoleIn[0].interpoleForm.playPosFrames += 1;
                    
                }
                
                //if (FInterpoleIn[0].PlayPosFrames > 100) FInterpoleIn[0].PlayPosFrames = 0;
                catch { FLogger.Log(LogType.Debug, "nope"); }
            }
            


        }
    }

