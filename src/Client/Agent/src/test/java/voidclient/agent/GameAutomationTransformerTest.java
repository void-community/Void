package voidclient.agent;

import java.io.InputStream;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.Label;
import org.objectweb.asm.tree.FieldNode;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

public final class GameAutomationTransformerTest {
    @Test
    public void executesOnlyBeforeFrameNotInsideConstructionRenderingOrInlineCallbacks() throws Exception {
        String name = FrameHost.class.getName().replace('.', '/');
        ClassNode type = new ClassNode();
        try (InputStream input = FrameHost.class.getResourceAsStream("/" + name + ".class")) {
            new ClassReader(input).accept(type, 0);
        }
        FramePlan plan = new FramePlan(name, "loop", "()V", "frame", "()V");
        Assert.assertTrue(GameAutomationTransformer.instrument(type, plan, false));
        Assert.assertFalse(GameAutomationTransformer.instrument(type, plan, false));
        for (MethodNode method : type.methods)
            for (AbstractInsnNode instruction : method.instructions)
                if (instruction instanceof MethodInsnNode) {
                    MethodInsnNode call = (MethodInsnNode) instruction;
                    if (call.owner.equals(VoidClientAgent.GameHooksName)) {
                        Assert.assertEquals("loop", method.name);
                        call.owner = Probe.class.getName().replace('.', '/');
                    }
                }
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        Probe.calls = 0;
        Probe.rendering = false;
        Class<?> host = new BytecodeLoader().define(writer.toByteArray());
        Object instance = host.getConstructor().newInstance();
        Assert.assertEquals(0, Probe.calls);
        host.getMethod("loop").invoke(instance);
        Assert.assertEquals(2, Probe.calls);
        Assert.assertFalse(Probe.rendering);
    }

    @Test
    public void reachesTheLoopAGameAlreadyRunsThroughTheStartOfItsFrame() throws Exception {
        // Agents attached to a running game retransform a client class whose loop never returns, so a call added there never runs.
        String name = FrameHost.class.getName().replace('.', '/');
        ClassNode type = new ClassNode();
        try (InputStream input = FrameHost.class.getResourceAsStream("/" + name + ".class")) {
            new ClassReader(input).accept(type, 0);
        }
        FramePlan plan = new FramePlan(name, "loop", "()V", "frame", "()V");
        Assert.assertTrue(GameAutomationTransformer.instrument(type, plan, true));
        Assert.assertFalse(GameAutomationTransformer.instrument(type, plan, true));
        for (MethodNode method : type.methods)
            for (AbstractInsnNode instruction : method.instructions)
                if (instruction instanceof MethodInsnNode) {
                    MethodInsnNode call = (MethodInsnNode) instruction;
                    if (call.owner.equals(VoidClientAgent.GameHooksName)) {
                        Assert.assertEquals("frame", method.name);
                        call.owner = Probe.class.getName().replace('.', '/');
                    }
                }
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        Probe.calls = 0;
        Probe.rendering = false;
        Class<?> host = new BytecodeLoader().define(writer.toByteArray());
        Object instance = host.getConstructor().newInstance();
        host.getMethod("frame").invoke(instance);
        host.getMethod("frame").invoke(instance);
        Assert.assertEquals(2, Probe.calls);
        Assert.assertFalse(Probe.rendering);
    }

    @Test
    public void observesConnectionWithoutRevivingDiscardedConstructorLocals() throws Exception {
        ClassNode type = new ClassNode();
        type.version = Opcodes.V1_8;
        type.access = Opcodes.ACC_PUBLIC;
        type.name = "synthetic/DiscardedReceiver";
        type.superName = "java/lang/Object";
        type.fields.add(new FieldNode(Opcodes.ACC_PUBLIC, "connection", "Ljava/lang/Object;", null, null));
        type.fields.add(new FieldNode(0, "flag", "Z", null, null));
        MethodNode constructor = new MethodNode(Opcodes.ACC_PUBLIC, "<init>", "(Ljava/lang/Object;Z)V", null, null);
        constructor.visitVarInsn(Opcodes.ALOAD, 0);
        constructor.visitMethodInsn(Opcodes.INVOKESPECIAL, "java/lang/Object", "<init>", "()V", false);
        constructor.visitVarInsn(Opcodes.ALOAD, 0);
        constructor.visitVarInsn(Opcodes.ALOAD, 1);
        constructor.visitFieldInsn(Opcodes.PUTFIELD, type.name, "connection", "Ljava/lang/Object;");
        constructor.visitVarInsn(Opcodes.ALOAD, 0);
        constructor.visitVarInsn(Opcodes.ILOAD, 2);
        Label otherwise = new Label();
        Label end = new Label();
        constructor.visitJumpInsn(Opcodes.IFEQ, otherwise);
        constructor.visitInsn(Opcodes.ICONST_1);
        constructor.visitJumpInsn(Opcodes.GOTO, end);
        constructor.visitLabel(otherwise);
        constructor.visitFrame(Opcodes.F_FULL, 0, new Object[0], 1, new Object[] { type.name });
        constructor.visitInsn(Opcodes.ICONST_0);
        constructor.visitLabel(end);
        constructor.visitFrame(Opcodes.F_FULL, 0, new Object[0], 2, new Object[] { type.name, Opcodes.INTEGER });
        constructor.visitFieldInsn(Opcodes.PUTFIELD, type.name, "flag", "Z");
        constructor.visitInsn(Opcodes.RETURN);
        constructor.maxLocals = 3;
        constructor.maxStack = 2;
        type.methods.add(constructor);
        Assert.assertTrue(GameAutomationTransformer.instrumentRejections(type, java.util.Collections.<String>emptyList(), java.util.Collections.singleton("java/lang/Object")));
        Assert.assertFalse(GameAutomationTransformer.instrumentRejections(type, java.util.Collections.<String>emptyList(), java.util.Collections.singleton("java/lang/Object")));
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        Class<?> observed = new BytecodeLoader().define(writer.toByteArray());
        Object connection = new Object();
        Object listener = observed.getConstructor(Object.class, Boolean.TYPE).newInstance(connection, Boolean.TRUE);
        Assert.assertSame(connection, observed.getField("connection").get(listener));
    }

    @Test
    public void tracesHowAConnectionEnds() throws Exception {
        String name = TracedConnection.class.getName().replace('.', '/');
        ClassNode type = new ClassNode();
        try (InputStream input = TracedConnection.class.getResourceAsStream("/" + name + ".class")) {
            new ClassReader(input).accept(type, 0);
        }
        java.util.Set<String> reasons = GameAutomationTransformer.reasonTypes(java.util.Collections.singletonMap("synthetic/Listener",
            java.util.Collections.singletonList("onDisconnect(L" + Reason.class.getName().replace('.', '/') + ";)V")));
        Assert.assertTrue(GameAutomationTransformer.instrumentConnection(type, reasons));
        Assert.assertFalse(GameAutomationTransformer.instrumentConnection(type, reasons));
        for (MethodNode method : type.methods)
            for (AbstractInsnNode instruction : method.instructions)
                if (instruction instanceof MethodInsnNode && ((MethodInsnNode) instruction).owner.equals(VoidClientAgent.GameHooksName))
                    ((MethodInsnNode) instruction).owner = Probe.class.getName().replace('.', '/');
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        Probe.events.clear();
        Class<?> traced = new BytecodeLoader().define(writer.toByteArray());
        Object connection = traced.getConstructor().newInstance();
        Reason reason = new Reason();
        IllegalStateException failure = new IllegalStateException("closed");

        traced.getMethod("channelRead0", io.netty.channel.ChannelHandlerContext.class, String.class).invoke(connection, null, "packet");
        traced.getMethod("channelInactive", io.netty.channel.ChannelHandlerContext.class).invoke(connection, (Object) null);
        traced.getMethod("exceptionCaught", io.netty.channel.ChannelHandlerContext.class, Throwable.class).invoke(connection, null, failure);
        traced.getMethod("disconnect", Reason.class).invoke(connection, reason);
        traced.getMethod("listen", Object.class).invoke(connection, reason);

        Assert.assertEquals(java.util.Arrays.asList(
            java.util.Arrays.asList(ConnectionTrace.Read, connection, "packet"),
            java.util.Arrays.asList(ConnectionTrace.Inactive, connection, null),
            java.util.Arrays.asList(ConnectionTrace.Exception, connection, failure),
            java.util.Arrays.asList(ConnectionTrace.Disconnect, connection, reason)), Probe.events);
    }

    @Test
    public void leavesNetworkHandlersThatReadNoPacketsAlone() throws Exception {
        String name = FrameHost.class.getName().replace('.', '/');
        ClassNode type = new ClassNode();
        try (InputStream input = FrameHost.class.getResourceAsStream("/" + name + ".class")) {
            new ClassReader(input).accept(type, 0);
        }
        Assert.assertFalse(GameAutomationTransformer.instrumentConnection(type, java.util.Collections.singleton("java/lang/Object")));
    }

    @Test
    public void keepsTheDisconnectReasonBeforeTheChannelCloses() throws Exception {
        String name = ClosingConnection.class.getName().replace('.', '/');
        ClassNode type = new ClassNode();
        try (InputStream input = ClosingConnection.class.getResourceAsStream("/" + name + ".class")) {
            new ClassReader(input).accept(type, 0);
        }
        MethodNode disconnect = null;
        for (MethodNode method : type.methods)
            if ("disconnect".equals(method.name))
                disconnect = method;
        Assert.assertTrue(GameAutomationTransformer.recordReasonBeforeClose(type, disconnect));
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        final Class<?> closing = new BytecodeLoader().define(writer.toByteArray());
        final Object connection = closing.getConstructor().newInstance();
        final Object[] reasonAtClose = new Object[1];
        final boolean[] open = { true };
        closing.getField("channel").set(connection, java.lang.reflect.Proxy.newProxyInstance(getClass().getClassLoader(),
            new Class<?>[] { io.netty.channel.Channel.class }, new java.lang.reflect.InvocationHandler() {
                @Override
                public Object invoke(Object proxy, java.lang.reflect.Method method, Object[] arguments) throws Exception {
                    if ("isOpen".equals(method.getName()))
                        return Boolean.valueOf(open[0]);
                    if (!"close".equals(method.getName()))
                        throw new UnsupportedOperationException(method.getName());
                    reasonAtClose[0] = closing.getField("reason").get(connection);
                    open[0] = false;
                    return null;
                }
            }));
        Reason kick = new Reason();

        closing.getMethod("disconnect", Reason.class).invoke(connection, kick);
        Assert.assertSame(kick, reasonAtClose[0]);

        // A disconnect once the channel closed keeps the reason the game was told first.
        closing.getMethod("disconnect", Reason.class).invoke(connection, new Reason());
        Assert.assertSame(kick, closing.getField("reason").get(connection));
    }

    // The disconnect of Minecraft's connection, which keeps its reason only after it closes the channel.
    public static class ClosingConnection {
        public io.netty.channel.Channel channel;
        public Reason pending;
        public Reason reason;
        public void disconnect(Reason reason) {
            if (channel == null)
                this.pending = reason;
            if (channel.isOpen()) {
                channel.close();
                this.reason = reason;
            }
        }
    }

    public static class Reason {
    }

    // Netty's own handler is replaced by a stub on the test class path, and the hooks need only the shapes of netty's methods.
    public static class TracedConnection {
        public void channelRead0(io.netty.channel.ChannelHandlerContext context, String packet) {
        }
        public void channelInactive(io.netty.channel.ChannelHandlerContext context) {
        }
        public void exceptionCaught(io.netty.channel.ChannelHandlerContext context, Throwable cause) {
        }
        public void disconnect(Reason reason) {
        }
        public void listen(Object listener) {
        }
    }

    public static class FrameHost {
        public FrameHost() {
            getter();
        }
        public void loop() {
            for (int iteration = 0; iteration < 2; iteration++)
                frame();
        }
        public void frame() {
            Probe.rendering = true;
            Probe.inline(this);
            Probe.rendering = false;
        }
        public void getter() {
        }
    }

    public static class Probe {
        public static int calls;
        public static boolean rendering;
        public static void inline(final Object client) {
            java.util.concurrent.Executor executor = new java.util.concurrent.Executor() {
                public void execute(Runnable command) { command.run(); }
            };
            executor.execute(new Runnable() {
                public void run() {
                    try { client.getClass().getMethod("getter").invoke(client); }
                    catch (Exception exception) { throw new AssertionError(exception); }
                }
            });
        }
        public static final java.util.List<java.util.List<Object>> events = new java.util.ArrayList<java.util.List<Object>>();
        public static void connectionEvent(String event, Object connection, Object detail) {
            events.add(java.util.Arrays.asList(event, connection, detail));
        }
        public static void applyClient(Object client) {
            Assert.assertFalse("Automation must not run inside rendering", rendering);
            calls++;
        }
    }

    private static class BytecodeLoader extends ClassLoader {
        Class<?> define(byte[] bytes) {
            return defineClass(null, bytes, 0, bytes.length);
        }
    }
}
