package voidclient.agent;

import java.io.IOException;
import java.lang.instrument.Instrumentation;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.util.Base64;

public final class VoidClientAgent {
    private static boolean started;

    private VoidClientAgent() {
    }

    public static void premain(String arguments, Instrumentation instrumentation) {
        start(arguments, instrumentation, false);
    }

    public static void agentmain(String arguments, Instrumentation instrumentation) {
        start(arguments, instrumentation, true);
    }

    private static synchronized void start(String arguments, Instrumentation instrumentation, boolean retransformLoadedClasses) {
        if (started)
            return;

        try {
            AgentArguments parsedArguments = AgentArguments.parse(arguments);
            Tracker.initialize(instrumentation, parsedArguments.expectedName);
            GameAutomationController.initialize(instrumentation);
            instrumentation.addTransformer(new NettyReadInterestTransformer(), true);
            instrumentation.addTransformer(new DataFixerWarmupTransformer(), true);
            instrumentation.addTransformer(new PlayerTransformer());
            instrumentation.addTransformer(new GameAutomationTransformer(), true);

            if (retransformLoadedClasses)
                retransformLoadedClasses(instrumentation);

            ServerSocket serverSocket = new ServerSocket(0, 16, InetAddress.getLoopbackAddress());
            writeDescriptor(parsedArguments.descriptorPath, serverSocket.getLocalPort());

            Thread serverThread = new Thread(new AgentServer(serverSocket, parsedArguments.token), "Void client state agent");
            serverThread.setDaemon(true);
            serverThread.setPriority(Thread.MIN_PRIORITY);
            serverThread.start();
            started = true;
        } catch (Throwable exception) {
            System.err.println("Void client state agent failed to start: " + exception.getMessage());
        }
    }

    private static void retransformLoadedClasses(Instrumentation instrumentation) {
        for (Class<?> type : instrumentation.getAllLoadedClasses()) {
            if (!instrumentation.isModifiableClass(type) || type.isArray() || type.isPrimitive())
                continue;

            try {
                String className = type.getName().replace('.', '/');
                if (NettyReadInterestTransformer.ChannelName.equals(className) || DataFixerWarmupTransformer.BuilderName.equals(className)) {
                    instrumentation.retransformClasses(type);
                    continue;
                }

                java.security.ProtectionDomain protectionDomain = type.getProtectionDomain();
                java.security.CodeSource codeSource = protectionDomain == null ? null : protectionDomain.getCodeSource();
                GameAutomationIndex.IndexedCode index = GameAutomationIndex.index(codeSource == null ? null : codeSource.getLocation());

                if (index != null && (index.plan.frame.owner.equals(className) || index.plan.rejectionCallbacks.containsKey(className)))
                    instrumentation.retransformClasses(type);
            } catch (Throwable exception) {
                // Some JVM-generated classes report as modifiable but cannot actually be retransformed.
            }
        }
    }

    private static void writeDescriptor(Path descriptorPath, int port) throws IOException {
        Path parent = descriptorPath.getParent();

        if (parent != null)
            Files.createDirectories(parent);

        Path temporaryPath = Paths.get(descriptorPath.toString() + ".tmp");
        Files.write(temporaryPath, Integer.toString(port).getBytes(StandardCharsets.US_ASCII));

        try {
            Files.move(temporaryPath, descriptorPath, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
        } catch (IOException exception) {
            Files.move(temporaryPath, descriptorPath, StandardCopyOption.REPLACE_EXISTING);
        }
    }

    private static final class AgentArguments {
        private final Path descriptorPath;
        private final String token;
        private final String expectedName;

        private AgentArguments(Path descriptorPath, String token, String expectedName) {
            this.descriptorPath = descriptorPath;
            this.token = token;
            this.expectedName = expectedName;
        }

        private static AgentArguments parse(String arguments) {
            String descriptor = null;
            String token = null;
            String expectedName = null;

            for (String argument : arguments.split(";")) {
                int separatorIndex = argument.indexOf('=');

                if (separatorIndex < 1)
                    continue;

                String key = argument.substring(0, separatorIndex);
                String value = decode(argument.substring(separatorIndex + 1));

                if ("descriptor".equals(key))
                    descriptor = value;
                else if ("token".equals(key))
                    token = value;
                else if ("name".equals(key))
                    expectedName = value;
            }

            if (descriptor == null || token == null)
                throw new IllegalArgumentException("descriptor and token agent arguments are required");

            return new AgentArguments(Paths.get(descriptor), token, expectedName);
        }

        private static String decode(String value) {
            return new String(Base64.getUrlDecoder().decode(value), StandardCharsets.UTF_8);
        }
    }
}
