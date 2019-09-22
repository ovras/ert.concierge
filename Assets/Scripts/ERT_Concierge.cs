using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class ERT_Concierge : MonoBehaviour
{
    public int port;
    public string ipAddress;

    private void Start()
    {
        AsyncServer server = gameObject.AddComponent<AsyncServer>() as AsyncServer;
        AsyncServer.port = port;
        AsyncServer.ipAddress = ipAddress;
        AsyncServer.StartServer(server);
    }
}
