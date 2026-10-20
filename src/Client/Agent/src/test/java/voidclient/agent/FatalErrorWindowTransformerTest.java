package voidclient.agent;

import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Label;
import org.objectweb.asm.MethodVisitor;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

public final class FatalErrorWindowTransformerTest {
    private static final String Descriptor = "(Ljava/util/List;Ljava/nio/file/Path;Ljava/nio/file/Path;Ljava/nio/file/Path;)V";

    @Test
    public void fatalErrorWindowExitsBeforeItOpens() throws Exception {
        byte[] transformed = new FatalErrorWindowTransformer().transform(null, FatalErrorWindowTransformer.HandlerName, null, null, handler());
        ClassNode type = new ClassNode();
        new ClassReader(transformed).accept(type, 0);

        for (MethodNode method : type.methods) {
            if (!"displayFatalErrorAndExit".equals(method.name))
                continue;

            MethodInsnNode exit = null;

            for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null && exit == null; instruction = instruction.getNext())
                if (instruction instanceof MethodInsnNode && instruction.getOpcode() == Opcodes.INVOKESTATIC)
                    exit = (MethodInsnNode) instruction;

            Assert.assertNotNull(exit);
            Assert.assertEquals("java/lang/System.exit(I)V", exit.owner + '.' + exit.name + exit.desc);
        }

        // Linking verifies the rewritten method, which never runs here.
        Class<?> loaded = new ClassLoader(null) {
            Class<?> define(byte[] bytes) {
                return defineClass(null, bytes, 0, bytes.length);
            }
        }.define(transformed);
        Assert.assertNotNull(loaded.getDeclaredMethods());
    }

    @Test
    public void otherClassesAreLeftAlone() {
        Assert.assertNull(new FatalErrorWindowTransformer().transform(null, "net/neoforged/fml/loading/Other", null, null, handler()));
    }

    private static byte[] handler() {
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_FRAMES);
        writer.visit(Opcodes.V21, Opcodes.ACC_PUBLIC | Opcodes.ACC_FINAL, FatalErrorWindowTransformer.HandlerName, null, "java/lang/Object", null);
        MethodVisitor method = writer.visitMethod(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "displayFatalErrorAndExit", Descriptor, null, null);
        Label done = new Label();
        method.visitCode();
        method.visitVarInsn(Opcodes.ALOAD, 0);
        method.visitJumpInsn(Opcodes.IFNULL, done);
        method.visitVarInsn(Opcodes.ALOAD, 0);
        method.visitMethodInsn(Opcodes.INVOKEINTERFACE, "java/util/List", "clear", "()V", true);
        method.visitLabel(done);
        method.visitInsn(Opcodes.RETURN);
        method.visitMaxs(0, 0);
        method.visitEnd();
        writer.visitEnd();
        return writer.toByteArray();
    }
}
