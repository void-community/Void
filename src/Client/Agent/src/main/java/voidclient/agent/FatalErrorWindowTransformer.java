package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

/**
 * Ends the game where NeoForge's loader would show its fatal error window, such as for a mod that failed to load or a missing
 * dependency. The window waits for someone to click Quit Game, which nobody can in the container, so the game would stay open
 * without ever starting Minecraft. The loader has logged the errors, and saved a crash report when it has one, by then.
 */
final class FatalErrorWindowTransformer implements ClassFileTransformer {
    static final String HandlerName = "net/neoforged/fml/loading/ImmediateWindowHandler";
    private static final String MethodName = "displayFatalErrorAndExit";

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined, ProtectionDomain protectionDomain, byte[] classFileBuffer) {
        if (!HandlerName.equals(className))
            return null;

        ClassNode type = new ClassNode();
        new ClassReader(classFileBuffer).accept(type, 0);
        boolean changed = false;

        for (MethodNode method : type.methods) {
            if (!MethodName.equals(method.name))
                continue;

            InsnList exit = new InsnList();
            exit.add(new FieldInsnNode(Opcodes.GETSTATIC, "java/lang/System", "err", "Ljava/io/PrintStream;"));
            exit.add(new LdcInsnNode("Void client agent closed the game instead of NeoForge's fatal error window, which nobody can answer in the container"));
            exit.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "java/io/PrintStream", "println", "(Ljava/lang/String;)V", false));
            exit.add(new InsnNode(Opcodes.ICONST_1));
            exit.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "java/lang/System", "exit", "(I)V", false));
            method.instructions.insert(exit);
            changed = true;
        }

        if (!changed)
            return null;

        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        return writer.toByteArray();
    }
}
