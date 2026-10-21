package voidclient.agent;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

final class AgentServer implements Runnable {
    private final ServerSocket serverSocket;
    private final String token;

    AgentServer(ServerSocket serverSocket, String token) {
        this.serverSocket = serverSocket;
        this.token = token;
    }

    @Override
    public void run() {
        while (!serverSocket.isClosed()) {
            try {
                final Socket socket = serverSocket.accept();
                Thread requestThread = new Thread(new Runnable() {
                    @Override
                    public void run() {
                        serve(socket);
                    }
                }, "Void client agent request");
                requestThread.setDaemon(true);
                requestThread.start();
            } catch (IOException exception) {
                if (!serverSocket.isClosed())
                    System.err.println("Void client state agent request failed: " + exception.getMessage());
            }
        }
    }

    private void serve(Socket socket) {
        try {
            socket.setSoTimeout(2000);
            BufferedReader reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
            BufferedWriter writer = new BufferedWriter(new OutputStreamWriter(socket.getOutputStream(), StandardCharsets.UTF_8));
            String request = reader.readLine();
            String[] parts = request == null ? new String[0] : request.split("\\t", 4);
            String response;

            if (parts.length == 0 || !token.equals(parts[0]))
                response = "{\"status\":\"unavailable\",\"message\":\"Invalid tracker token\"}";
            else if (parts.length == 1 || (parts.length == 2 && "snapshot".equals(parts[1])))
                response = Tracker.snapshotJson();
            else if (parts.length == 2 && "connection".equals(parts[1]))
                response = GameAutomationController.connectionJson();
            else if (parts.length == 2 && "ready".equals(parts[1]))
                response = GameAutomationController.readyJson();
            else if (parts.length == 2 && "disconnect".equals(parts[1]))
                response = GameAutomationController.disconnectJson();
            else if (parts.length == 4 && "connect".equals(parts[1]))
                response = connect(parts[2], parts[3]);
            else if (parts.length == 4 && "chat".equals(parts[1]))
                response = chat(parts[2], parts[3]);
            else if (parts.length == 3 && "cancel".equals(parts[1])) {
                GameAutomationController.cancel(parts[2]);
                response = "{\"status\":\"ok\",\"stage\":\"request.cancelled\"}";
            }
            else
                response = "{\"status\":\"unavailable\",\"message\":\"Unknown agent command\"}";

            writer.write(response);
            writer.newLine();
            writer.flush();
        } catch (IOException exception) {
            System.err.println("Void client state agent response failed: " + exception.getMessage());
        } finally {
            try {
                socket.close();
            } catch (IOException exception) {
                // The request is already complete.
            }
        }
    }

    private String connect(String requestId, String encodedAddress) {
        try {
            String address = new String(Base64.getUrlDecoder().decode(encodedAddress), StandardCharsets.UTF_8);

            if (address.isEmpty())
                return "{\"status\":\"unavailable\",\"stage\":\"request.validation\",\"message\":\"The server address is empty\"}";

            return GameAutomationController.connectJson(requestId, address);
        } catch (IllegalArgumentException exception) {
            return "{\"status\":\"unavailable\",\"stage\":\"request.validation\",\"message\":\"The server address is not valid Base64URL\"}";
        }
    }

    private String chat(String requestId, String encodedMessage) {
        try {
            String message = new String(Base64.getUrlDecoder().decode(encodedMessage), StandardCharsets.UTF_8);

            if (message.trim().isEmpty())
                return "{\"status\":\"unavailable\",\"stage\":\"request.validation\",\"message\":\"The chat message is empty\"}";

            return GameAutomationController.chatJson(requestId, message);
        } catch (IllegalArgumentException exception) {
            return "{\"status\":\"unavailable\",\"stage\":\"request.validation\",\"message\":\"The chat message is not valid Base64URL\"}";
        }
    }
}
