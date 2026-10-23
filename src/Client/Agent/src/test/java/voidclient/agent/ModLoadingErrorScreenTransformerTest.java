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

public final class ModLoadingErrorScreenTransformerTest {
    private static final String Descriptor = "(Ljava/lang/Object;Ljava/util/List;Ljava/io/File;)Ljava/io/File;";

    @Test
    public void savedModLoadingCrashReportExitsBeforeTheErrorScreen() throws Exception {
        // Issue #1617: NeoForge 1.21.1 showed its Error loading mods screen, and the game was reported ready behind it.
        for (String className : new String[] { "net/neoforged/neoforge/logging/CrashReportExtender", "net/minecraftforge/fmllegacy/CrashReportExtender" }) {
            byte[] transformed = new ModLoadingErrorScreenTransformer().transform(null, className, null, null, extender(className));
            ClassNode type = new ClassNode();
            new ClassReader(transformed).accept(type, 0);

            for (MethodNode method : type.methods) {
                if (!"dumpModLoadingCrashReport".equals(method.name))
                    continue;

                int returns = 0;

                // Every return leaves through the exit, after the report was saved.
                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (instruction.getOpcode() != Opcodes.ARETURN)
                        continue;

                    MethodInsnNode exit = (MethodInsnNode) instruction.getPrevious();
                    Assert.assertEquals("java/lang/System.exit(I)V", exit.owner + '.' + exit.name + exit.desc);
                    returns++;
                }

                Assert.assertEquals(2, returns);
            }

            // Linking verifies the rewritten method, which never runs here.
            Class<?> loaded = new ClassLoader(null) {
                Class<?> define(byte[] bytes) {
                    return defineClass(null, bytes, 0, bytes.length);
                }
            }.define(transformed);
            Assert.assertNotNull(loaded.getDeclaredMethods());
        }
    }

    @Test
    public void otherClassesAreLeftAlone() {
        Assert.assertNull(new ModLoadingErrorScreenTransformer().transform(null, "net/neoforged/neoforge/logging/Other", null, null, extender("net/neoforged/neoforge/logging/Other")));
        Assert.assertNull(new ModLoadingErrorScreenTransformer().transform(null, "example/CrashReportExtender", null, null, extender("example/CrashReportExtender")));
    }

    private static byte[] extender(String className) {
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_FRAMES);
        writer.visit(Opcodes.V21, Opcodes.ACC_PUBLIC | Opcodes.ACC_FINAL, className, null, "java/lang/Object", null);
        MethodVisitor method = writer.visitMethod(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "dumpModLoadingCrashReport", Descriptor, null, null);
        Label saved = new Label();
        method.visitCode();
        method.visitVarInsn(Opcodes.ALOAD, 1);
        method.visitJumpInsn(Opcodes.IFNONNULL, saved);
        method.visitInsn(Opcodes.ACONST_NULL);
        method.visitInsn(Opcodes.ARETURN);
        method.visitLabel(saved);
        method.visitVarInsn(Opcodes.ALOAD, 2);
        method.visitInsn(Opcodes.ARETURN);
        method.visitMaxs(0, 0);
        method.visitEnd();
        writer.visitEnd();
        return writer.toByteArray();
    }
}
