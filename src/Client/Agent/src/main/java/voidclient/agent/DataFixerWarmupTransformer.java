package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.Type;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.LookupSwitchInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.TableSwitchInsnNode;
import org.objectweb.asm.tree.TypeInsnNode;
import org.objectweb.asm.tree.VarInsnNode;

final class DataFixerWarmupTransformer implements ClassFileTransformer {
    static final String BuilderName = "com/mojang/datafixers/DataFixerBuilder";
    private static final String FixerName = "com/mojang/datafixers/DataFixerUpper";

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined, ProtectionDomain protectionDomain, byte[] classFileBuffer) {
        if (!BuilderName.equals(className))
            return null;

        ClassNode type = new ClassNode();
        new ClassReader(classFileBuffer).accept(type, 0);
        MethodNode unoptimized = null;
        for (MethodNode method : type.methods)
            if ("buildUnoptimized".equals(method.name) && Type.getArgumentTypes(method.desc).length == 0 && (method.access & Opcodes.ACC_STATIC) == 0)
                unoptimized = method;

        boolean changed = false;
        for (MethodNode method : type.methods) {
            if (!("build".equals(method.name) || "buildOptimized".equals(method.name)) || !method.desc.contains("Ljava/util/concurrent/Executor;") || (method.access & Opcodes.ACC_STATIC) != 0)
                continue;

            InsnList replacement = new InsnList();
            if (unoptimized != null && Type.getReturnType(method.desc).equals(Type.getReturnType(unoptimized.desc))) {
                replacement.add(new VarInsnNode(Opcodes.ALOAD, 0));
                replacement.add(new MethodInsnNode(Opcodes.INVOKESPECIAL, BuilderName, unoptimized.name, unoptimized.desc, false));
            } else {
                MethodInsnNode constructor = findLegacyConstructor(method);
                if (constructor == null)
                    continue;
                // Preserve the original schema/fix copies and converter construction.
                // Only the eager cache-warming loop following it is omitted.
                while (true) {
                    AbstractInsnNode instruction = method.instructions.getFirst();
                    method.instructions.remove(instruction);
                    replacement.add(instruction);
                    if (instruction == constructor)
                        break;
                }
            }

            // DataFixerUpper.update still compiles and caches migration rules on demand.
            replacement.add(new InsnNode(Opcodes.ARETURN));
            method.instructions = replacement;
            method.tryCatchBlocks.clear();
            method.localVariables = null;
            method.visibleLocalVariableAnnotations = null;
            method.invisibleLocalVariableAnnotations = null;
            changed = true;
        }

        if (!changed)
            return null;

        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        return writer.toByteArray();
    }

    private static MethodInsnNode findLegacyConstructor(MethodNode method) {
        if (!"build".equals(method.name) || !method.tryCatchBlocks.isEmpty())
            return null;
        AbstractInsnNode first = nextInstruction(method.instructions.getFirst());
        if (!(first instanceof TypeInsnNode) || first.getOpcode() != Opcodes.NEW || !FixerName.equals(((TypeInsnNode) first).desc))
            return null;

        for (AbstractInsnNode instruction = first; instruction != null; instruction = instruction.getNext()) {
            if (instruction instanceof JumpInsnNode || instruction instanceof TableSwitchInsnNode || instruction instanceof LookupSwitchInsnNode)
                return null;
            if (!(instruction instanceof MethodInsnNode))
                continue;
            MethodInsnNode call = (MethodInsnNode) instruction;
            if (!FixerName.equals(call.owner) || !"<init>".equals(call.name))
                continue;
            AbstractInsnNode stored = nextInstruction(call.getNext());
            AbstractInsnNode last = previousInstruction(method.instructions.getLast());
            AbstractInsnNode returned = last == null ? null : previousInstruction(last.getPrevious());
            if (stored instanceof VarInsnNode && stored.getOpcode() == Opcodes.ASTORE && last != null && last.getOpcode() == Opcodes.ARETURN && returned instanceof VarInsnNode && returned.getOpcode() == Opcodes.ALOAD && ((VarInsnNode) returned).var == ((VarInsnNode) stored).var)
                return call;
            return null;
        }
        return null;
    }

    private static AbstractInsnNode nextInstruction(AbstractInsnNode instruction) {
        while (instruction != null && instruction.getOpcode() < 0)
            instruction = instruction.getNext();
        return instruction;
    }

    private static AbstractInsnNode previousInstruction(AbstractInsnNode instruction) {
        while (instruction != null && instruction.getOpcode() < 0)
            instruction = instruction.getPrevious();
        return instruction;
    }
}
