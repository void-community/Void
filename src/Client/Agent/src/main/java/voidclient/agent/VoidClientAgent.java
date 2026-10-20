package voidclient.agent;

import java.io.IOException;
import java.io.InputStream;
import java.lang.instrument.Instrumentation;
import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.net.BindException;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.util.Arrays;
import java.util.Base64;
import java.util.Collections;
import java.util.List;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.jar.JarOutputStream;

public final class VoidClientAgent {
    static final String GameHooksName = "voidclient/bootstrap/GameHooks";
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
            instrumentation.addTransformer(new FatalErrorWindowTransformer(), true);

            if (installGameHooks(instrumentation)) {
                instrumentation.addTransformer(new PlayerTransformer());
                instrumentation.addTransformer(new GameAutomationTransformer(), true);
                instrumentation.addTransformer(new FatalErrorReportTransformer(), true);
            }

            if (retransformLoadedClasses)
                retransformLoadedClasses(instrumentation);

            List<ServerSocket> serverSockets = bindLoopbackServerSockets();
            writeDescriptor(parsedArguments.descriptorPath, serverSockets.get(0).getLocalPort());

            for (ServerSocket serverSocket : serverSockets) {
                Thread serverThread = new Thread(new AgentServer(serverSocket, parsedArguments.token), "Void client state agent " + serverSocket.getInetAddress().getHostAddress());
                serverThread.setDaemon(true);
                serverThread.setPriority(Thread.MIN_PRIORITY);
                serverThread.start();
            }

            started = true;
        } catch (Throwable exception) {
            System.err.println("Void client state agent failed to start: " + exception.getMessage());
        }
    }

    /**
     * Puts GameHooks on the bootstrap class path, where the calls the transformers inject can link from every class loader. Game
     * classes stay untouched when that fails, so the game still runs without player tracking hooks and UI automation.
     */
    private static boolean installGameHooks(Instrumentation instrumentation) {
        try {
            instrumentation.appendToBootstrapClassLoaderSearch(new JarFile(writeGameHooksJar().toFile()));
            MethodHandles.Lookup lookup = MethodHandles.lookup();
            MethodType single = MethodType.methodType(void.class, Object.class);
            MethodType pair = MethodType.methodType(void.class, Object.class, Object.class);
            MethodType fatalError = MethodType.methodType(void.class, Throwable.class, Path.class, Path.class);
            Class.forName(GameHooksName.replace('/', '.'), true, null)
                .getMethod("install", MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class)
                .invoke(null,
                    lookup.findStatic(Tracker.class, "registerPlayer", single),
                    lookup.findStatic(GameAutomationController.class, "applyClient", single),
                    lookup.findStatic(GameAutomationController.class, "connectionListenerCreated", pair),
                    lookup.findStatic(GameAutomationController.class, "connectionRejected", pair),
                    lookup.findStatic(FatalErrorReports.class, "save", fatalError));
            return true;
        } catch (Throwable exception) {
            GameAutomationController.recordIndexFailure(GameAutomationController.GameHooksFailure, exception.toString());
            return false;
        }
    }

    static Path writeGameHooksJar() throws IOException {
        String entryName = GameHooksName + ".class";
        Path jarPath = Files.createTempFile("void-client-game-hooks", ".jar");
        jarPath.toFile().deleteOnExit();

        try (InputStream input = VoidClientAgent.class.getResourceAsStream("/" + entryName);
             JarOutputStream output = new JarOutputStream(Files.newOutputStream(jarPath))) {
            if (input == null)
                throw new IOException("The agent jar does not contain " + entryName);

            output.putNextEntry(new JarEntry(entryName));
            byte[] buffer = new byte[8192];
            int length;

            while ((length = input.read(buffer)) != -1)
                output.write(buffer, 0, length);

            output.closeEntry();
        }

        return jarPath;
    }

    /**
     * Listens on the IPv4 and IPv6 loopbacks with one port, because launchers differ in which loopback the JVM prefers
     * (NeoForge sets java.net.preferIPv6Addresses=system) and a container may provide only one of them.
     */
    static List<ServerSocket> bindLoopbackServerSockets() throws IOException {
        InetAddress ipv4Loopback = InetAddress.getByAddress(new byte[] { 127, 0, 0, 1 });
        InetAddress ipv6Loopback = InetAddress.getByAddress(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 });

        if (!canBind(ipv6Loopback))
            return Collections.singletonList(new ServerSocket(0, 16, ipv4Loopback));

        if (!canBind(ipv4Loopback))
            return Collections.singletonList(new ServerSocket(0, 16, ipv6Loopback));

        for (int attempt = 1; attempt <= 10; attempt++) {
            ServerSocket ipv4ServerSocket = new ServerSocket(0, 16, ipv4Loopback);

            try {
                return Arrays.asList(ipv4ServerSocket, new ServerSocket(ipv4ServerSocket.getLocalPort(), 16, ipv6Loopback));
            } catch (BindException portTaken) {
                ipv4ServerSocket.close();
            }
        }

        // The client API tries the IPv4 loopback first, so it still connects when no shared port was free.
        return Collections.singletonList(new ServerSocket(0, 16, ipv4Loopback));
    }

    private static boolean canBind(InetAddress address) {
        try (ServerSocket probe = new ServerSocket(0, 1, address)) {
            return probe.isBound();
        } catch (IOException exception) {
            return false;
        }
    }

    private static void retransformLoadedClasses(Instrumentation instrumentation) {
        for (Class<?> type : instrumentation.getAllLoadedClasses()) {
            if (!instrumentation.isModifiableClass(type) || type.isArray() || type.isPrimitive())
                continue;

            try {
                String className = type.getName().replace('.', '/');
                if (NettyReadInterestTransformer.ChannelName.equals(className) || DataFixerWarmupTransformer.BuilderName.equals(className)
                    || FatalErrorWindowTransformer.HandlerName.equals(className) || FatalErrorReportTransformer.ReportingName.equals(className)) {
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
