using System;
using System.IO;
using System.Text;
using System.Net.Sockets;

namespace HTPCAVRVolume.AVRDevices
{
    class DenonMarantzDevice : IAVRDevice
    {
        private string _IP;

        public DenonMarantzDevice(string IP)
        {
            _IP = IP;
        }

        public void VolUp()   { SendCmd("MVUP"); }
        public void VolDown() { SendCmd("MVDOWN"); }

        public void ToggleMute()
        {
            bool currentlyMuted = QueryMute();
            SendCmd(currentlyMuted ? "MUOFF" : "MUON");
        }

        private bool QueryMute()
        {
            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(_IP, 23);
                if (!connectTask.Wait(2000))
                    throw new TimeoutException($"Connection to {_IP}:23 timed out");

                var stream = client.GetStream();
                stream.ReadTimeout = 1500;

                byte[] query = Encoding.ASCII.GetBytes("MU?\r");
                stream.Write(query, 0, query.Length);

                var sb = new StringBuilder();
                byte[] buffer = new byte[64];
                try
                {
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        string so_far = sb.ToString();
                        if (so_far.Contains("MUON")) return true;
                        if (so_far.Contains("MUOFF")) return false;
                        if (sb.Length > 512) break;
                    }
                }
                catch (System.IO.IOException) { } // ReadTimeout — no response

                return false; // assume unmuted if we can't determine
            }
            finally
            {
                client.Close();
            }
        }

        public void PowerOn()  { SendCmd("PWON"); }
        public void PowerOff() { SendCmd("PWSTANDBY"); }

        private void SendCmd(string cmd)
        {
            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(_IP, 23);
                if (!connectTask.Wait(2000))
                    throw new TimeoutException($"Connection to {_IP}:23 timed out");

                byte[] cmdBytes = Encoding.ASCII.GetBytes(cmd + "\r");
                client.GetStream().Write(cmdBytes, 0, cmdBytes.Length);
            }
            finally
            {
                client.Close();
            }
        }
    }
}
