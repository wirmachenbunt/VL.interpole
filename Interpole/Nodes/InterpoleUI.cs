using System;
using System.Threading;
using System.ComponentModel.Composition;
using System.Windows.Forms;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using Interpole;
using VVVV.Core.Logging;
using System.Collections.Generic;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "Interpole UI", Category = "Interpole", AutoEvaluate = true)]

    public class InterpoleUI : IPluginEvaluate, IDisposable
    {
        Thread FUIThread;
        InterpoleForm FMyForm;

        public List<InterpoleForm.Clip> clipList; //master clip list

        [Output("Out")]
        ISpread<InterpoleUI> FInterpoleOut;

        public InterpoleUI()
        {
            FUIThread = new Thread(InitializeForm);
            FUIThread.SetApartmentState(ApartmentState.STA);            
            FUIThread.Start();

            clipList = new List<InterpoleForm.Clip>();
        }

        public void Dispose()
        {
            if (FMyForm != null)
                FMyForm.Invoke((Action)CleanupForm);
        }

        void InitializeForm()
        {
            FMyForm = new InterpoleForm();
            
            // Use ShowDialog as it starts its own main loop - using Show alone the window just disappears
            FMyForm.ShowDialog();
        }

        void CleanupForm()
        {
            FMyForm.Dispose();
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (FMyForm == null)
                return; // Form not created yet
            else
            {
                clipList = FMyForm.clipList;
            }
            //FMyForm.Invoke((Action)InvalidateForm);
            //FMyForm.Invoke((Action)(() => FMyLabel.Text = value));       
            
        }
    }
}