package org.example.runtimeprobe;

import android.os.Bundle;

public final class ExceptionProbe {
    private static final class Holder { int value; }
    public static final class CustomException extends RuntimeException {
        public CustomException(String message) { super(message); }
    }

    public static int exactCatch() {
        try { throw new IllegalStateException("exact"); }
        catch (IllegalStateException error) { return error.getMessage().length(); }
    }

    public static int superCatch() {
        try { throw new CustomException("custom"); }
        catch (RuntimeException error) { return 2; }
    }

    public static int handlerOrder() {
        try { throw new CustomException("ordered"); }
        catch (CustomException error) { return 13; }
        catch (RuntimeException error) { return 14; }
    }

    public static int errorVsException() {
        try { throw new NoSuchMethodError("missing"); }
        catch (Exception ignored) { return -1; }
        catch (Error error) { return 3; }
    }

    public static int throwableCatch() {
        try { throw new NoSuchMethodError(); }
        catch (Throwable error) { return 4; }
    }

    private static void level3() { throw new CustomException("three"); }
    private static void level2() { level3(); }
    private static void level1() { level2(); }
    public static int unwindThreeFrames() {
        try { level1(); return -1; }
        catch (CustomException error) { return 5; }
    }

    private static void rethrow() {
        try { throw new CustomException("same"); }
        catch (CustomException error) { throw error; }
    }
    public static int catchRethrow() {
        try { rethrow(); return -1; }
        catch (CustomException error) { return error.getMessage().length(); }
    }

    public static int rethrowIdentity() {
        CustomException original = new CustomException("identity");
        try { throw original; }
        catch (CustomException first) {
            try { throw first; }
            catch (CustomException second) { return original == second ? 15 : -1; }
        }
    }

    public static int throwableToString() {
        return new CustomException("text").toString().startsWith("org.example.runtimeprobe.ExceptionProbe$CustomException: text") ? 16 : -1;
    }

    public static int throwNull() {
        try { throw (RuntimeException)null; }
        catch (NullPointerException error) { return 7; }
    }

    public static int divideInt(int divisor) {
        try { return 10 / divisor; }
        catch (ArithmeticException error) { return 8; }
    }

    public static int divideLong(long divisor) {
        try { return (int)(10L / divisor); }
        catch (ArithmeticException error) { return 9; }
    }

    public static int nullReceiver() {
        try { return ((String)null).length(); }
        catch (NullPointerException error) { return 10; }
    }

    public static int arrayBounds(int index) {
        try { int[] values = new int[1]; return values[index]; }
        catch (ArrayIndexOutOfBoundsException error) { return 11; }
    }

    public static int classCast() {
        try { return ((String)(Object)new Bundle()).length(); }
        catch (ClassCastException error) { return 12; }
    }

    public static int negativeArray(int size) {
        try { return new int[size].length; }
        catch (NegativeArraySizeException error) { return 17; }
    }

    public static int nullField() {
        try { return ((Holder)null).value; }
        catch (NullPointerException error) { return 18; }
    }

    public static int nullArray() {
        try { int[] values = null; return values[0]; }
        catch (NullPointerException error) { return 19; }
    }

    public static int catchAllFinally() {
        int value = 0;
        try { throw new CustomException("finally"); }
        finally { value++; }
    }

    public static void uncaught() { throw new CustomException("sanitized"); }
    public void onCreate(Bundle ignored) { uncaught(); }
}
