package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.VarInsnNode;

/**
 * Hands every fatal error NeoForge's loader reports to {@link FatalErrorReports}, which saves the ones that come without a crash
 * report. The loader funnels all of them through one method, whose arguments are the error, the game directory, the log file and
 * the crash report it saved, if any.
 */
final class FatalErrorReportTransformer implements ClassFileTransformer {
    static final String ReportingName = "net/neoforged/fml/startup/FatalErrorReporting";
    private static final String MethodName = "reportFatalError";
    private static final String MethodDescriptor = "(Ljava/lang/Throwable;Ljava/nio/file/Path;Ljava/nio/file/Path;Ljava/nio/file/Path;)V";

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined, ProtectionDomain protectionDomain, byte[] classFileBuffer) {
        if (!ReportingName.equals(className))
            return null;

        ClassNode type = new ClassNode();
        new ClassReader(classFileBuffer).accept(type, 0);
        boolean changed = false;

        for (MethodNode method : type.methods) {
            if (!MethodName.equals(method.name) || !MethodDescriptor.equals(method.desc) || (method.access & Opcodes.ACC_STATIC) == 0)
                continue;

            InsnList report = new InsnList();
            report.add(new VarInsnNode(Opcodes.ALOAD, 0));
            report.add(new VarInsnNode(Opcodes.ALOAD, 1));
            report.add(new VarInsnNode(Opcodes.ALOAD, 3));
            report.add(new MethodInsnNode(Opcodes.INVOKESTATIC, VoidClientAgent.GameHooksName, "fatalErrorReported",
                "(Ljava/lang/Throwable;Ljava/nio/file/Path;Ljava/nio/file/Path;)V", false));
            method.instructions.insert(report);
            changed = true;
        }

        if (!changed)
            return null;

        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        return writer.toByteArray();
    }
}
