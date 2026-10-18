package voidclient.agent;

import java.net.Inet4Address;
import java.net.Inet6Address;
import java.net.ServerSocket;
import java.util.List;
import org.junit.Assert;
import org.junit.Test;

public final class LoopbackBindingTest {
    @Test
    public void listensOnEveryAvailableLoopbackWithOnePort() throws Exception {
        List<ServerSocket> serverSockets = VoidClientAgent.bindLoopbackServerSockets();

        try {
            Assert.assertTrue(serverSockets.size() == 1 || serverSockets.size() == 2);

            int port = serverSockets.get(0).getLocalPort();

            for (ServerSocket serverSocket : serverSockets) {
                Assert.assertEquals(port, serverSocket.getLocalPort());
                Assert.assertTrue(serverSocket.getInetAddress().isLoopbackAddress());
            }

            if (serverSockets.size() == 2) {
                Assert.assertTrue(serverSockets.get(0).getInetAddress() instanceof Inet4Address);
                Assert.assertTrue(serverSockets.get(1).getInetAddress() instanceof Inet6Address);
            }
        } finally {
            for (ServerSocket serverSocket : serverSockets)
                serverSocket.close();
        }
    }
}
