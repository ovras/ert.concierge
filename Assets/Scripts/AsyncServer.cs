using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Asynchronously reads the client data
public class DataBuffer
{
    public Socket clientSocket;
    public const int bufferSize = 8192;
    public byte[] buffer = new byte[bufferSize];
    public string str = "";
}

public class AsyncServer : MonoBehaviour
{
    public static int port = 60000;
    public static string ipAddress = "131.215.144.245";

    public static Dictionary<int, Socket> pythonClients = new Dictionary<int, Socket>();
    public static Dictionary<int, Socket> unityClients = new Dictionary<int, Socket>();

    public static float tickTime = 0.1f;
    
    static Socket listener; // So we can stop server from receiving connections when the script is disabled
    
    public enum SocketKind
    {
        Python,
        Unity
    }

    public AsyncServer() 
    {
        Debug.Log("AsyncServer: New server created!");
    }

    public static void StartServer(MonoBehaviour instance)
    {
        Debug.Log("Starting server...");

        IPAddress address = IPAddress.Parse(ipAddress);//ipHost.AddressList[0];
        IPEndPoint ipEnd = new IPEndPoint(address, port);

        listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        // Bind to local endpoint and look for connections
        try
        {
            listener.Bind(ipEnd);
            listener.Listen(100);
            
            Debug.Log("AsyncServer: Waiting for connections...");
            listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
        }
        catch (Exception e)
        {
            Debug.Log("AsyncServer: " + e);
        }
    }

    #region ASYNCHRONOUS CALLBACKS
    public static void AcceptCallback(IAsyncResult result)
    {
        // Get socket to handle the client request
        Socket listener = (Socket)result.AsyncState;
        Socket handler = listener.EndAccept(result);

        // Create the data buffer
        DataBuffer buffer = new DataBuffer();
        buffer.clientSocket = handler;

        // Send the ready message and wait for the handshake message
        NetworkData readyMsg = new NetworkData();
        readyMsg.ID_Receiver = new int[0];
        readyMsg.Data_Type = "String";
        readyMsg.Data_Label = "Message";
        readyMsg.Data = "Ready";

        Send(handler, JsonUtility.ToJson(readyMsg) + "\n");
        handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(HandshakeCallback), buffer);

        // Continue to listen for connections
        listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
    }

    public static void HandshakeCallback(IAsyncResult result)
    {
        string id = "";

        DataBuffer buffer = (DataBuffer)result.AsyncState;
        Socket handler = buffer.clientSocket;

        // Read the data from the client socket
        int bytesRead = handler.EndReceive(result);

        if (bytesRead > 0)
        {
            buffer.str += Encoding.ASCII.GetString(buffer.buffer, 0, bytesRead);
            Debug.Log("AsyncServer: Connect request with ID: " + buffer.str);

            // Check that this is the end of the message
            if (buffer.str.IndexOf("\n", StringComparison.CurrentCulture) == -1)
            {
                // Need to continue getting more data
                Debug.Log("AsyncServer: Incomplete data was received. Getting more data...");
                handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(HandshakeCallback), buffer);
                return;
            }

            int index = buffer.str.IndexOf("\n", StringComparison.CurrentCulture);
            id = buffer.str.Substring(0, index);
            buffer.str = buffer.str.Remove(0, index + 1);
        }

        if (id.Contains("PYTHON"))
        {
            try
            {
                pythonClients.Add(int.Parse(id.Substring(6).Trim()), handler);
            }
            catch (Exception e)
            {
                Debug.LogError("AsyncServer: Python client tried to connect with invalid ID " + id.Substring(6).Trim());
                return;
            }
            Debug.Log("AsyncServer: Python client connected.");

            // Done with handshake protocol, can now begin reading information normally
            buffer.buffer = new byte[DataBuffer.bufferSize];
            NetworkData readyMsg = new NetworkData();
            readyMsg.ID_Receiver = GetIds(SocketKind.Unity);
            readyMsg.Data_Type = "String";
            readyMsg.Data_Label = "Message";
            readyMsg.Data = "Ready";

            Send(handler, JsonUtility.ToJson(readyMsg) + "\n");
            handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(PythonCallback), buffer);
        }
        else if (id.Contains("UNITY"))
        {
            try
            {
                unityClients.Add(int.Parse(id.Substring(5).Trim()), handler);
            }
            catch (Exception e)
            {
                Debug.LogError("AsyncServer: Unity client tried to connect with invalid ID " + id.Substring(6).Trim());
                return;
            }
            buffer.buffer = new byte[DataBuffer.bufferSize];
            NetworkData readyMsg = new NetworkData();
            readyMsg.ID_Receiver = GetIds(SocketKind.Python);
            readyMsg.Data_Type = "String";
            readyMsg.Data_Label = "Message";
            readyMsg.Data = "Ready";

            Send(handler, JsonUtility.ToJson(readyMsg) + "\n");
            Debug.Log("AsyncServer: Unity client connected.");

            // Done with handshake protocol, can now begin reading information normally
            handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(UnityCallback), buffer);
        }
        else
        {
            Debug.Log("AsyncServer: Unrecognized handshake message: " + id);
        }
    }

    public static void PythonCallback(IAsyncResult result)
    {
        DataBuffer buffer = (DataBuffer)result.AsyncState;
        Socket handler = buffer.clientSocket;

        int bytesRead = handler.EndReceive(result);

        ReadCallback(SocketKind.Python, buffer, handler, bytesRead);

        // Reset the socket buffer and ask for more information
        buffer.buffer = new byte[DataBuffer.bufferSize];
        NetworkData readyMsg = new NetworkData();
        readyMsg.ID_Receiver = GetIds(SocketKind.Unity);
        readyMsg.Data_Type = "String";
        readyMsg.Data_Label = "Message";
        readyMsg.Data = "Ready";

        // Wait for tickTime seconds between ready calls
        System.Threading.Thread.Sleep((int)(tickTime * 1000));
        Send(handler, JsonUtility.ToJson(readyMsg) + "\n");
        handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(PythonCallback), buffer);
    }

    public static void UnityCallback(IAsyncResult result)
    {
        DataBuffer buffer = (DataBuffer)result.AsyncState;
        Socket handler = buffer.clientSocket;

        int bytesRead = handler.EndReceive(result);

        ReadCallback(SocketKind.Unity, buffer, handler, bytesRead);

        // Reset the socket buffer and ask for more information
        buffer.buffer = new byte[DataBuffer.bufferSize];
        NetworkData readyMsg = new NetworkData();
        readyMsg.ID_Receiver = GetIds(SocketKind.Python);
        readyMsg.Data_Type = "String";
        readyMsg.Data_Label = "Message";
        readyMsg.Data = "Ready";

        // Wait for tickTime seconds between ready calls
        System.Threading.Thread.Sleep((int)(tickTime * 1000));
        Send(handler, JsonUtility.ToJson(readyMsg) + "\n");
        handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(UnityCallback), buffer);
    }

    private static void SendCallback(IAsyncResult result)
    {
        try
        {
            Socket sock = (Socket)result.AsyncState;
            int bytesSent = sock.EndSend(result);
        }
        catch (Exception e)
        {
            Debug.Log("AsyncServer: " + e);
        }
    }
    #endregion

    #region SOCKET HELPER METHODS
    private static bool Send(Socket sock, string message)
    {
        // Don't send empty messages to help keep the socket clear
        if (message == "" || message == "\n") return true; 

        byte[] data = Encoding.ASCII.GetBytes(message);
        try
        {
            sock.BeginSend(data, 0, data.Length, 0, new AsyncCallback(SendCallback), sock);
            return true;
        }
        catch (SocketException e)
        {
            Debug.Log("AsyncServer: Socket was disconnected.");
            sock.Close();
            return false;
        }
    }

    private static bool IsConnected(Socket s)
    {
        bool poll = s.Poll(1000, SelectMode.SelectRead);
        bool avail = s.Available == 0;

        if (poll && avail) return false;

        return true;
    }
    #endregion

    #region DATA PROCESSING
    private static void ReadCallback(SocketKind type, DataBuffer buffer, Socket handler, int bytesRead)
    {
        // Read the data from the client socket
        if (bytesRead > 0)
        {
            buffer.str += Encoding.ASCII.GetString(buffer.buffer, 0, bytesRead);

            // Check that this is the end of the message
            if (buffer.str.IndexOf("}", StringComparison.CurrentCulture) == -1)
            {
                // Need to continue getting more data
                Debug.Log("AsyncServer: Incomplete data was received. Getting more data...");

                if (type == SocketKind.Python) handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(PythonCallback), buffer);
                else handler.BeginReceive(buffer.buffer, 0, DataBuffer.bufferSize, 0, new AsyncCallback(UnityCallback), buffer);

                return;
            }

            // Process each line separately until we have no full lines remaining
            while (buffer.str.IndexOf("}", StringComparison.CurrentCulture) != -1)
            {
                int index = buffer.str.IndexOf("}", StringComparison.CurrentCulture);
                string line = buffer.str.Substring(0, index + 1);
                buffer.str = buffer.str.Remove(0, index + 2);

                HandleLine(type, line);
            }
        }
    }

    private static void HandleLine(SocketKind type, string message)
    {
        NetworkData messageData = JsonUtility.FromJson<NetworkData>(message);

        int senderId = messageData.ID_Sender;
        int[] recvIds = messageData.ID_Receiver;

        if (type == SocketKind.Python)
        {
            List<int> toRemove = new List<int>();
            foreach (int id in recvIds)
            {
                if (unityClients.ContainsKey(id))
                {
                    Socket s = unityClients[id];
                    bool sent = Send(s, message + "\n");
                    if (!sent) toRemove.Add(id);
                }
            }

            foreach (int s in toRemove)
            {
                unityClients.Remove(s);
                Debug.LogWarning("AsyncServer: Removed a Unity client from the list.");
            }
        }

        else if (type == SocketKind.Unity)
        {
            List<int> toRemove = new List<int>();
            foreach (int id in messageData.ID_Receiver)
            {
                if (pythonClients.ContainsKey(id))
                {
                    Socket s = pythonClients[id];
                    bool sent = Send(s, message + "\n");
                    if (!sent) toRemove.Add(id);
                }
            }

            foreach (int s in toRemove)
            {
                pythonClients.Remove(s);
                Debug.LogWarning("AsyncServer: Removed a Python client from the list.");
            }
        }
        else
        {
            Debug.LogError("AsyncServer: Unrecognized client type " + type.ToString() + ". Message will not be sent.");
        }
    }

    // Unused, sends the ready message on a timer instead of after each callback
    private IEnumerator AskForInfo()
    {
        while (true)
        {
            NetworkData readyMsg = new NetworkData();
            readyMsg.Data_Type = "String";
            readyMsg.Data_Label = "Message";
            readyMsg.Data = "Ready";
            readyMsg.ID_Sender = 10000; // Server ID

            readyMsg.ID_Receiver = GetIds(SocketKind.Unity);
            List<int> pythonToRemove = new List<int>();
            foreach (KeyValuePair<int, Socket> entry in pythonClients)
            {
                bool sent = Send(entry.Value, JsonUtility.ToJson(readyMsg) + "\n");
                if (!sent) pythonToRemove.Add(entry.Key);
            }

            readyMsg.ID_Receiver = GetIds(SocketKind.Python);
            List<int> unityToRemove = new List<int>();
            foreach (KeyValuePair<int, Socket> entry in unityClients)
            {
                bool sent = Send(entry.Value, JsonUtility.ToJson(readyMsg) + "\n");
                if (!sent) unityToRemove.Add(entry.Key);
            }

            foreach (int s in pythonToRemove) pythonClients.Remove(s);
            foreach (int s in unityToRemove) unityClients.Remove(s);

            yield return new WaitForSeconds(tickTime);
        }
    }
    #endregion

    private static int[] GetIds(SocketKind socket)
    {
        int[] ids;

        int counter = 0;

        Dictionary<int, Socket> dict;
        if (socket == SocketKind.Unity) dict = unityClients;
        else dict = pythonClients;

        ids = new int[dict.Count];
        foreach (KeyValuePair<int, Socket> pair in dict)
        {
            ids[counter] = pair.Key;
            counter++;
        }

        return ids;
    }

    public static int Main(String[] args)
    {
        return 0;
    }

    private void OnDisable()
    {
        Debug.Log("UnityClient: Concierge was disabled. Shutting down all socket connections...");
        foreach (KeyValuePair<int, Socket> pair in unityClients) pair.Value.Close();
        foreach (KeyValuePair<int, Socket> pair in pythonClients) pair.Value.Close();
        listener.Close();
    }
}