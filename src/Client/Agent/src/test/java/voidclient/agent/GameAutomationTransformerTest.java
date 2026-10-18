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
        Assert.assertTrue(GameAutomationTransformer.instrument(type, plan));
        Assert.assertFalse(GameAutomationTransformer.instrument(type, plan));
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
