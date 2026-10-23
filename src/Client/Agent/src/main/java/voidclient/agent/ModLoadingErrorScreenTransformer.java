package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

/**
 * Ends the game once Forge's or NeoForge's loader saved the crash report for mods that failed to load, where it would next show
 * its in-game Error loading mods screen. Only Quit Game helps on that screen, which nobody can click in the container, and the
 * game behind it can't be played. Forge from 1.16 and NeoForge up to 1.21.11 save that report with one method of a
 * CrashReportExtender, whose package changed between versions.
 */
final class ModLoadingErrorScreenTransformer implements ClassFileTransformer {
    private static final String ExtenderName = "/CrashReportExtender";
    private static final String MethodName = "dumpModLoadingCrashReport";

    static boolean isExtender(String className) {
        return className != null && className.endsWith(ExtenderName) && (className.startsWith("net/minecraftforge/") || className.startsWith("net/neoforged/"));
    }

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined, ProtectionDomain protectionDomain, byte[] classFileBuffer) {
        if (!isExtender(className))
            return null;

        ClassNode type = new ClassNode();
        new ClassReader(classFileBuffer).accept(type, 0);
        boolean changed = false;

        for (MethodNode method : type.methods) {
            if (!MethodName.equals(method.name))
                continue;

            for (AbstractInsnNode instruction : method.instructions.toArray()) {
                if (instruction.getOpcode() != Opcodes.ARETURN)
                    continue;

                InsnList exit = new InsnList();
                exit.add(new FieldInsnNode(Opcodes.GETSTATIC, "java/lang/System", "err", "Ljava/io/PrintStream;"));
                exit.add(new LdcInsnNode("Void client agent closed the game instead of the loader's Error loading mods screen, which nobody can answer in the container"));
                exit.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "java/io/PrintStream", "println", "(Ljava/lang/String;)V", false));
                exit.add(new InsnNode(Opcodes.ICONST_1));
                exit.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "java/lang/System", "exit", "(I)V", false));
                method.instructions.insertBefore(instruction, exit);
                changed = true;
            }
        }

        if (!changed)
            return null;

        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        return writer.toByteArray();
    }
}
