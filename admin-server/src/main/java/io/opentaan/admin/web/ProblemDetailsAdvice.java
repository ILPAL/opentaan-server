package io.opentaan.admin.web;

import java.util.Comparator;

import io.opentaan.admin.accounts.AccountNotFoundException;
import io.opentaan.admin.economy.AdjustmentAlreadyDecidedException;
import io.opentaan.admin.economy.AdjustmentNotFoundException;
import io.opentaan.admin.economy.CharacterNotFoundException;
import io.opentaan.admin.economy.IdempotencyKeyReusedException;
import io.opentaan.admin.economy.InvalidAdjustmentException;
import io.opentaan.admin.economy.SelfDecisionException;
import jakarta.validation.ConstraintViolation;
import jakarta.validation.ConstraintViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

/**
 * Maps exceptions to RFC 9457 problem details. Services throw exceptions that say
 * what went wrong; only this class knows which HTTP status that means.
 */
@RestControllerAdvice
class ProblemDetailsAdvice {

    @ExceptionHandler
    ProblemDetail accountNotFound(AccountNotFoundException exception) {
        return problem(HttpStatus.NOT_FOUND, "Account not found", exception);
    }

    @ExceptionHandler
    ProblemDetail characterNotFound(CharacterNotFoundException exception) {
        return problem(HttpStatus.NOT_FOUND, "Character not found", exception);
    }

    @ExceptionHandler
    ProblemDetail adjustmentNotFound(AdjustmentNotFoundException exception) {
        return problem(HttpStatus.NOT_FOUND, "Adjustment not found", exception);
    }

    @ExceptionHandler
    ProblemDetail invalidAdjustment(InvalidAdjustmentException exception) {
        return problem(HttpStatus.BAD_REQUEST, "Invalid adjustment", exception);
    }

    /** Four-eyes: refusing the requester is an authorization decision, hence 403. */
    @ExceptionHandler
    ProblemDetail selfDecision(SelfDecisionException exception) {
        return problem(HttpStatus.FORBIDDEN, "Another operator must decide this adjustment", exception);
    }

    @ExceptionHandler
    ProblemDetail alreadyDecided(AdjustmentAlreadyDecidedException exception) {
        return problem(HttpStatus.CONFLICT, "Adjustment already decided", exception);
    }

    @ExceptionHandler
    ProblemDetail idempotencyKeyReused(IdempotencyKeyReusedException exception) {
        return problem(HttpStatus.CONFLICT, "Idempotency key reused", exception);
    }

    /** Two operators decided the same adjustment at once; the other decision was committed first. */
    @ExceptionHandler
    ProblemDetail changedConcurrently(ObjectOptimisticLockingFailureException exception) {
        var problem = ProblemDetail.forStatusAndDetail(HttpStatus.CONFLICT,
                "Someone else changed this at the same time. Reload it and try again.");
        problem.setTitle("Changed concurrently");
        return problem;
    }

    /** A parameter broke a constraint declared in the OpenAPI contract, such as {@code minimum: 1}. */
    @ExceptionHandler
    ProblemDetail constraintViolated(ConstraintViolationException exception) {
        var detail = exception.getConstraintViolations().stream()
                .sorted(Comparator.comparing(violation -> violation.getPropertyPath().toString()))
                .map(ProblemDetailsAdvice::describe)
                .reduce((a, b) -> a + "; " + b)
                .orElse("Invalid request");
        var problem = ProblemDetail.forStatusAndDetail(HttpStatus.BAD_REQUEST, detail);
        problem.setTitle("Invalid request");
        return problem;
    }

    private static ProblemDetail problem(HttpStatus status, String title, RuntimeException exception) {
        var problem = ProblemDetail.forStatusAndDetail(status, exception.getMessage());
        problem.setTitle(title);
        return problem;
    }

    private static String describe(ConstraintViolation<?> violation) {
        var path = violation.getPropertyPath().toString();
        var parameter = path.substring(path.lastIndexOf('.') + 1);
        return parameter + " " + violation.getMessage();
    }
}
